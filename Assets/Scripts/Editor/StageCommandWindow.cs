using System.Collections.Generic;
using Rush.Combat;
using Rush.Stage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.EditorTools
{
    /// <summary>
    /// 프로젝트 총괄 에디터 윈도우. 에셋 셋업, 씬 셋업, 데이터 검증, 플레이 모드 모니터/제어를
    /// 한 곳에서 처리한다. 실제 로직은 RushSetupActions에 있다.
    /// </summary>
    public class StageCommandWindow : EditorWindow
    {
        static readonly Color windowBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color panelBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color panelBorderColor = new Color(0.17f, 0.17f, 0.17f, 1f);
        static readonly Color headerBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color accentColor = new Color(0.36f, 0.36f, 0.36f, 1f);
        static readonly Color subtleTextColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        static readonly Color warnTextColor = new Color(1f, 0.6f, 0.2f, 1f);
        static readonly Color okTextColor = new Color(0.45f, 0.85f, 0.5f, 1f);

        const int MaxStatusLines = 8;

        readonly List<string> _statusLines = new List<string>();

        Label _statusLabel;
        VisualElement _assetSection;
        VisualElement _sceneSection;
        VisualElement _validationList;
        Label _runtimeStats;
        Button _playButton;
        VisualElement _runtimeControls;

        [MenuItem("Rush/Stage Command Center")]
        public static void Open()
        {
            var window = GetWindow<StageCommandWindow>();
            window.titleContent = new GUIContent("Stage Command Center");
            window.minSize = new Vector2(420f, 560f);
        }

        void OnEnable()
        {
            RushSetupActions.Reported += OnReported;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnDisable()
        {
            RushSetupActions.Reported -= OnReported;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        void OnReported(string message)
        {
            _statusLines.Add(message);

            if (_statusLines.Count > MaxStatusLines)
                _statusLines.RemoveAt(0);

            if (_statusLabel != null)
                _statusLabel.text = string.Join("\n", _statusLines);
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            RefreshPlayButton();
            RefreshSetupEnabled();
        }

        /// <summary>플레이 중에는 에셋/씬 셋업을 막는다 (씬 교체가 플레이를 깨뜨린다).</summary>
        void RefreshSetupEnabled()
        {
            bool editable = !EditorApplication.isPlayingOrWillChangePlaymode;

            if (_assetSection != null)
                _assetSection.SetEnabled(editable);

            if (_sceneSection != null)
                _sceneSection.SetEnabled(editable);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.backgroundColor = windowBackground;

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            scroll.contentContainer.style.paddingLeft = 10f;
            scroll.contentContainer.style.paddingRight = 10f;
            scroll.contentContainer.style.paddingTop = 10f;
            scroll.contentContainer.style.paddingBottom = 10f;
            root.Add(scroll);

            _assetSection = BuildAssetSection();
            _sceneSection = BuildSceneSection();

            scroll.Add(_assetSection);
            scroll.Add(_sceneSection);
            scroll.Add(BuildValidationSection());
            scroll.Add(BuildRuntimeSection());
            scroll.Add(BuildStatusSection());

            RefreshPlayButton();
            RefreshSetupEnabled();

            // 플레이 모드 상태를 주기 폴링해 런타임 모니터를 갱신한다
            root.schedule.Execute(RefreshRuntimeStats).Every(300);
        }

        // ---------- 섹션: 에셋 셋업 ----------

        VisualElement BuildAssetSection()
        {
            var section = CreateSectionShell("에셋 셋업", "ASSETS", out var body);

            body.Add(MakeDescription("더미 프리팹은 전부 큐브로 생성된다. 이후 프리팹만 교체하면 리소스가 갈아끼워진다."));

            var allButton = MakeButton("전체 에셋 셋업 (원클릭)", () =>
            {
                RushSetupActions.CreateAllAssets();
            });
            allButton.style.height = 28f;
            body.Add(allButton);

            var row = MakeButtonRow();
            row.Add(MakeButton("더미 프리팹", RushSetupActions.CreateDummyPrefabs));
            row.Add(MakeButton("타워 데이터", RushSetupActions.CreateTowerData));
            row.Add(MakeButton("몬스터 데이터", RushSetupActions.CreateMonsterData));
            body.Add(row);

            var row2 = MakeButtonRow();
            row2.Add(MakeButton("스테이지/난이도", RushSetupActions.CreateStageAndDifficultyData));
            row2.Add(MakeButton("PanelSettings", () => RushSetupActions.CreatePanelSettings()));
            body.Add(row2);

            return section;
        }

        // ---------- 섹션: 씬 셋업 ----------

        VisualElement BuildSceneSection()
        {
            var section = CreateSectionShell("씬 셋업", "SCENE", out var body);

            body.Add(MakeDescription("Stage01 씬을 만들고 지형/경로/슬롯/컨트롤러/UI를 배치한 뒤 참조를 전부 연결한다. 이미 있으면 빠진 것만 보충한다."));

            var setupButton = MakeButton("씬 골격 생성 + 참조 연결", RushSetupActions.SetupScene);
            setupButton.style.height = 28f;
            body.Add(setupButton);

            var row = MakeButtonRow();

            row.Add(MakeButton("Stage01 씬 열기", OpenStageScene));

            row.Add(MakeButton("경로 비주얼 다시 베이크", () =>
            {
                var route = FindFirstObjectByType<PathRoute>();

                if (route == null)
                {
                    OnReported("씬에 PathRoute가 없음 - 먼저 씬 셋업 실행");
                    return;
                }

                RushSetupActions.BakePathVisual(route);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }));

            body.Add(row);

            var row2 = MakeButtonRow();

            row2.Add(MakeButton("StageController 선택", () =>
            {
                var stage = FindFirstObjectByType<StageController>();

                if (stage == null)
                {
                    OnReported("씬에 StageController가 없음");
                    return;
                }

                Selection.activeGameObject = stage.gameObject;
                EditorGUIUtility.PingObject(stage.gameObject);
            }));

            body.Add(row2);

            return section;
        }

        void OpenStageScene()
        {
            const string scenePath = "Assets/RushGame/Scenes/Stage01.unity";

            // 씬 파일이 없는 상태에서 OpenScene을 부르면 ArgumentException으로 끝난다
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                OnReported("Stage01 씬이 아직 없음 - '씬 골격 생성 + 참조 연결'을 먼저 실행");
                return;
            }

            // 저장 확인에서 취소하면 씬을 바꾸지 않는다
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                OnReported("씬 저장이 취소되어 열기를 중단함");
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        // ---------- 섹션: 검증 ----------

        VisualElement BuildValidationSection()
        {
            var section = CreateSectionShell("검증", "VALIDATE", out var body);

            body.Add(MakeButton("데이터 / 씬 검증 실행", RunValidation));

            _validationList = new VisualElement();
            _validationList.style.marginTop = 6f;
            body.Add(_validationList);

            return section;
        }

        void RunValidation()
        {
            var issues = RushSetupActions.Validate();

            _validationList.Clear();

            if (issues.Count == 0)
            {
                var ok = new Label("문제 없음");
                ok.style.color = okTextColor;
                ok.style.fontSize = 11;
                _validationList.Add(ok);
                return;
            }

            foreach (var issue in issues)
            {
                var label = new Label("- " + issue);
                label.style.color = warnTextColor;
                label.style.fontSize = 11;
                label.style.whiteSpace = WhiteSpace.Normal;
                _validationList.Add(label);
            }
        }

        // ---------- 섹션: 런타임 ----------

        VisualElement BuildRuntimeSection()
        {
            var section = CreateSectionShell("런타임 모니터 / 제어", "PLAY", out var body);

            _playButton = MakeButton("플레이 시작", TogglePlayMode);
            _playButton.style.height = 26f;
            body.Add(_playButton);

            _runtimeStats = new Label("플레이 모드가 아닙니다");
            _runtimeStats.style.color = subtleTextColor;
            _runtimeStats.style.fontSize = 11;
            _runtimeStats.style.whiteSpace = WhiteSpace.Normal;
            _runtimeStats.style.marginTop = 8f;
            _runtimeStats.style.marginBottom = 8f;
            body.Add(_runtimeStats);

            _runtimeControls = MakeButtonRow();
            _runtimeControls.Add(MakeButton("조기소환", () =>
            {
                var stage = FindFirstObjectByType<StageController>();

                if (stage != null)
                    stage.CallNextWaveEarly();
            }));
            _runtimeControls.Add(MakeButton("골드 +100", () =>
            {
                var stage = FindFirstObjectByType<StageController>();

                if (stage != null)
                    stage.AddGold(100);
            }));
            _runtimeControls.Add(MakeButton("배속 전환", () =>
            {
                var stage = FindFirstObjectByType<StageController>();

                if (stage != null)
                    stage.CycleSpeed();
            }));
            _runtimeControls.Add(MakeButton("재시작", () =>
            {
                var stage = FindFirstObjectByType<StageController>();

                if (stage != null)
                    stage.RestartStage();
            }));
            body.Add(_runtimeControls);

            return section;
        }

        void TogglePlayMode()
        {
            EditorApplication.isPlaying = !EditorApplication.isPlaying;
        }

        void RefreshPlayButton()
        {
            if (_playButton == null)
                return;

            if (EditorApplication.isPlaying)
                _playButton.text = "플레이 정지";
            else
                _playButton.text = "플레이 시작";
        }

        void RefreshRuntimeStats()
        {
            if (_runtimeStats == null)
                return;

            if (!Application.isPlaying)
            {
                _runtimeStats.text = "플레이 모드가 아닙니다";
                _runtimeControls.SetEnabled(false);
                return;
            }

            _runtimeControls.SetEnabled(true);

            var stage = FindFirstObjectByType<StageController>();

            if (stage == null)
            {
                _runtimeStats.text = "StageController를 찾을 수 없음";
                return;
            }

            _runtimeStats.text =
                $"페이즈 {stage.Phase} | 웨이브 {stage.WaveNumber}/{stage.TotalWaves}" +
                $" | 골드 {stage.Gold} | 생명 {stage.Life} | 배속 {stage.CurrentSpeed:0.#}x" +
                $"\n몬스터 {MonsterRegistry.Active.Count} | 병사 {Soldier.Active.Count}" +
                $" | 다음 웨이브 {Mathf.CeilToInt(stage.NextWaveIn)}초 | 조기소환 보너스 +{stage.EarlyCallBonus}G";
        }

        // ---------- 섹션: 상태 로그 ----------

        VisualElement BuildStatusSection()
        {
            var section = CreateSectionShell("작업 로그", "STATUS", out var body);

            _statusLabel = new Label("아직 실행한 작업 없음");
            _statusLabel.style.color = subtleTextColor;
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            body.Add(_statusLabel);

            return section;
        }

        // ---------- 레이아웃 헬퍼 ----------

        VisualElement CreateSectionShell(string title, string badge, out VisualElement bodyContainer)
        {
            var shell = new VisualElement();
            shell.style.flexDirection = FlexDirection.Column;
            shell.style.backgroundColor = panelBackground;
            shell.style.overflow = Overflow.Hidden;
            shell.style.marginBottom = 10f;
            shell.style.borderLeftWidth = 1f;
            shell.style.borderRightWidth = 1f;
            shell.style.borderTopWidth = 1f;
            shell.style.borderBottomWidth = 1f;
            shell.style.borderLeftColor = panelBorderColor;
            shell.style.borderRightColor = panelBorderColor;
            shell.style.borderTopColor = panelBorderColor;
            shell.style.borderBottomColor = panelBorderColor;

            shell.Add(CreateSectionHeader(title, badge));
            shell.Add(CreateSectionAccentBar());

            bodyContainer = new VisualElement();
            bodyContainer.style.flexGrow = 1f;
            bodyContainer.style.flexDirection = FlexDirection.Column;
            bodyContainer.style.paddingLeft = 14f;
            bodyContainer.style.paddingRight = 14f;
            bodyContainer.style.paddingTop = 12f;
            bodyContainer.style.paddingBottom = 14f;
            bodyContainer.style.backgroundColor = panelBackground;

            shell.Add(bodyContainer);

            return shell;
        }

        VisualElement CreateSectionHeader(string title, string badgeText)
        {
            var header = new VisualElement();
            header.style.height = 40f;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.backgroundColor = headerBackground;
            header.style.paddingLeft = 14f;
            header.style.paddingRight = 14f;

            var leftGroup = new VisualElement();
            leftGroup.style.flexDirection = FlexDirection.Row;
            leftGroup.style.alignItems = Align.Center;

            var accent = new VisualElement();
            accent.style.width = 2f;
            accent.style.height = 18f;
            accent.style.backgroundColor = accentColor;
            accent.style.marginRight = 8f;
            leftGroup.Add(accent);

            var titleLabel = new Label(title);
            titleLabel.style.color = Color.white;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.fontSize = 12;
            leftGroup.Add(titleLabel);

            header.Add(leftGroup);

            if (!string.IsNullOrEmpty(badgeText))
            {
                var badge = new Label(badgeText.ToUpperInvariant());
                badge.style.color = subtleTextColor;
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                badge.style.fontSize = 9;
                badge.style.paddingLeft = 8f;
                badge.style.paddingRight = 8f;
                badge.style.paddingTop = 2f;
                badge.style.paddingBottom = 2f;
                header.Add(badge);
            }

            return header;
        }

        VisualElement CreateSectionAccentBar()
        {
            var bar = new VisualElement();
            bar.style.height = 1f;
            bar.style.backgroundColor = panelBorderColor;

            return bar;
        }

        static Button MakeButton(string text, System.Action onClick)
        {
            var button = new Button(onClick);
            button.text = text;
            button.style.flexGrow = 1f;
            button.style.marginBottom = 4f;

            return button;
        }

        static VisualElement MakeButtonRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 2f;

            return row;
        }

        static Label MakeDescription(string text)
        {
            var label = new Label(text);
            label.style.color = new Color(0.78f, 0.78f, 0.78f, 1f);
            label.style.fontSize = 11;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 8f;

            return label;
        }
    }
}
