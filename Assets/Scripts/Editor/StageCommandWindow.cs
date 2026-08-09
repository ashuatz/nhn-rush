using System.Collections.Generic;
using Rush.Combat;
using Rush.Data;
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

        const string LuckPreviewName = "__LuckFxPreview";
        const float LuckPreviewSeconds = 0.9f;

        readonly List<string> _statusLines = new List<string>();

        static readonly string[] PreviewTowers = { "Tower_Archer", "Tower_Mage", "Tower_Artillery" };

        readonly List<Vector3[]> _previewPaths = new List<Vector3[]>();

        Label _statusLabel;
        VisualElement _assetSection;
        VisualElement _sceneSection;
        VisualElement _validationList;
        Label _previewInfo;
        VisualElement _extrasBody;
        string _previewTower = PreviewTowers[0];
        bool _previewEnabled;
        bool _extrasPendingSave;

        /// <summary>
        /// UI를 만드는 동안 컨트롤이 초기값을 잡으며 변경 콜백을 쏘는 경우가 있다.
        /// 그대로 두면 사용자가 만지지도 않은 값이 에셋에 저장되므로 구성 중에는 콜백을 막는다.
        /// </summary>
        bool _buildingExtrasUI;
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

            // 창을 닫아도 씬 뷰 콜백이 남지 않게 반드시 해제한다
            SceneView.duringSceneGui -= OnSceneGui;
            _previewEnabled = false;
            _previewPaths.Clear();
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

            if (change == PlayModeStateChange.EnteredEditMode)
            {
                SavePendingExtras();
                RebuildExtrasUI();
            }
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
            scroll.Add(BuildMotionSection());
            scroll.Add(BuildExtrasSection());
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
            row.Add(MakeButton("아트 모델 적용", RushSetupActions.ApplyArtModels));
            row.Add(MakeButton("타워 데이터", RushSetupActions.CreateTowerData));
            row.Add(MakeButton("몬스터 데이터", RushSetupActions.CreateMonsterData));
            body.Add(row);

            var row2 = MakeButtonRow();
            row2.Add(MakeButton("스테이지/난이도", RushSetupActions.CreateStageAndDifficultyData));
            row2.Add(MakeButton("보상 데이터", () => RushSetupActions.CreateRewardAssets()));
            row2.Add(MakeButton("PanelSettings", () => RushSetupActions.CreatePanelSettings()));
            body.Add(row2);

            var row3 = MakeButtonRow();
            row3.Add(MakeButton("캐릭터 머티리얼 배선", RushSetupActions.BindCharacterMaterials));
            row3.Add(MakeButton("행운 연출 미리보기", PreviewLuckFx));
            body.Add(row3);

            var balanceButton = MakeButton("Balance Board 열기 (수치 조절)", BalanceBoardWindow.Open);
            body.Add(balanceButton);

            return section;
        }

        /// <summary>
        /// 행운 발동(C15) 연출을 에디트 모드에서 한 번 재생해 본다.
        /// 씬 뷰 중심에 임시 인스턴스를 띄워 수동 시뮬레이션하고, 재생이 끝나면 스스로 지운다.
        /// 저장되지 않도록 DontSave로 두므로 씬을 더럽히지 않는다.
        /// </summary>
        static void PreviewLuckFx()
        {
            RushSetupActions.EnsureFxPrefabs();

            var prefab = RushSetupActions.LoadLuckSparkPrefab();

            if (prefab == null)
            {
                Debug.LogWarning("[Rush] 행운 연출 프리팹이 없다. 셰이더(Rush/FX/Luck Ray)를 찾지 못했을 수 있다.");
                return;
            }

            var stale = GameObject.Find(LuckPreviewName);

            if (stale != null)
                DestroyImmediate(stale);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = LuckPreviewName;
            instance.hideFlags = HideFlags.DontSave;
            instance.transform.position = LuckPreviewPosition();

            var particles = instance.GetComponent<ParticleSystem>();

            if (particles == null)
            {
                DestroyImmediate(instance);
                return;
            }

            Selection.activeGameObject = instance;

            double startedAt = EditorApplication.timeSinceStartup;

            EditorApplication.CallbackFunction step = null;
            step = () =>
            {
                if (instance == null)
                {
                    EditorApplication.update -= step;
                    return;
                }

                float elapsed = (float)(EditorApplication.timeSinceStartup - startedAt);

                if (elapsed > LuckPreviewSeconds)
                {
                    EditorApplication.update -= step;
                    DestroyImmediate(instance);
                    SceneView.RepaintAll();

                    return;
                }

                particles.Simulate(elapsed, true, true);
                SceneView.RepaintAll();
            };

            EditorApplication.update += step;
        }

        static Vector3 LuckPreviewPosition()
        {
            var view = SceneView.lastActiveSceneView;

            if (view == null)
                return Vector3.zero;

            return view.pivot;
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

            row.Add(MakeButton("씬 레이아웃 정리", RushSetupActions.NormalizeSceneLayout));

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

        // ---------- 섹션: 공격 연출 ----------

        VisualElement BuildMotionSection()
        {
            var section = CreateSectionShell("공격 연출", "MOTION", out var body);

            body.Add(MakeDescription("발사체 궤적을 씬 뷰에 그려서 플레이하지 않고 확인한다. 슬롯을 선택하면 그 위치에서, 아니면 첫 슬롯에서 경로까지 쏜다."));

            var picker = new DropdownField("타워", new List<string>(PreviewTowers), 0);
            picker.style.marginBottom = 6f;
            picker.RegisterValueChangedCallback(evt =>
            {
                _previewTower = evt.newValue;
                RebuildPreview();
                RebuildExtrasUI();
            });
            body.Add(picker);

            var toggle = new Toggle("씬 뷰 궤적 미리보기");
            toggle.style.marginBottom = 6f;
            toggle.RegisterValueChangedCallback(evt => SetPreviewEnabled(evt.newValue));
            body.Add(toggle);

            var row = MakeButtonRow();
            row.Add(MakeButton("궤적 다시 뽑기", RebuildPreview));
            row.Add(MakeButton("연출 프리셋 덮어쓰기", () =>
            {
                RushSetupActions.ApplyMotionPresets(force: true);
                RebuildPreview();
            }));
            body.Add(row);

            _previewInfo = new Label("미리보기 꺼짐");
            _previewInfo.style.color = subtleTextColor;
            _previewInfo.style.fontSize = 11;
            _previewInfo.style.whiteSpace = WhiteSpace.Normal;
            _previewInfo.style.marginTop = 6f;
            body.Add(_previewInfo);

            return section;
        }

        // ---------- 섹션: 추가 발사 (개발자 옵션) ----------

        VisualElement BuildExtrasSection()
        {
            var section = CreateSectionShell("추가 발사", "EXTRA", out var body);

            body.Add(MakeDescription("위 '공격 연출'에서 고른 타워에 적용된다. 실제 발동은 보상 C13(연발 장전) / C14(처형 예포)가 제어하며, 여기 켜기는 보상 없이 연출을 보기 위한 개발자 강제 켜기다 (기본 꺼짐)."));
            body.Add(MakeDescription("플레이 중에 바꿔도 다음 발사부터 바로 반영된다 (이미 날아가는 발사체는 발사 시점 설정 유지). 플레이 중 조정한 값은 플레이를 끝낼 때 에셋에 저장된다. 단, 플레이 도중 스크립트가 재컴파일되면 디스크 값으로 되돌아간다."));

            _extrasBody = new VisualElement();
            body.Add(_extrasBody);

            RebuildExtrasUI();

            return section;
        }

        void RebuildExtrasUI()
        {
            if (_extrasBody == null)
                return;

            _buildingExtrasUI = true;

            try
            {
                BuildExtrasControls();
            }
            finally
            {
                _buildingExtrasUI = false;
            }
        }

        void BuildExtrasControls()
        {
            _extrasBody.Clear();

            var data = LoadPreviewTowerData();

            if (data == null)
            {
                _extrasBody.Add(new Label($"{_previewTower} 데이터를 찾을 수 없음"));
                return;
            }

            if (data.Extras == null)
                data.Extras = new AttackExtras();

            var extras = data.Extras;

            _extrasBody.Add(MakeSubHeader("확률 발사 (공격 시 확률로 추가탄)"));

            AddToggle(_extrasBody, "사용", extras.ProcEnabled, value =>
            {
                extras.ProcEnabled = value;
                MarkExtrasDirty(data);
            });

            AddSlider(_extrasBody, "발동 확률", extras.ProcChance, 0f, 1f, value =>
            {
                extras.ProcChance = value;
                MarkExtrasDirty(data);
            });

            AddSliderInt(_extrasBody, "발수", extras.ProcCount, 1, 5, value =>
            {
                extras.ProcCount = value;
                MarkExtrasDirty(data);
            });

            AddSlider(_extrasBody, "피해 배율", extras.ProcDamageScale, 0.2f, 4f, value =>
            {
                extras.ProcDamageScale = value;
                MarkExtrasDirty(data);
            });

            _extrasBody.Add(MakeSubHeader("처치 시 발사 (주변 적으로 튀는 추격탄)"));

            AddToggle(_extrasBody, "사용", extras.OnKillEnabled, value =>
            {
                extras.OnKillEnabled = value;
                MarkExtrasDirty(data);
            });

            AddSliderInt(_extrasBody, "발수", extras.OnKillCount, 1, 6, value =>
            {
                extras.OnKillCount = value;
                MarkExtrasDirty(data);
            });

            AddSlider(_extrasBody, "피해 배율", extras.OnKillDamageScale, 0.1f, 2f, value =>
            {
                extras.OnKillDamageScale = value;
                MarkExtrasDirty(data);
            });

            AddSlider(_extrasBody, "탐색 반경", extras.OnKillSearchRadius, 2f, 10f, value =>
            {
                extras.OnKillSearchRadius = value;
                MarkExtrasDirty(data);
            });
        }

        TowerData LoadPreviewTowerData()
        {
            return AssetDatabase.LoadAssetAtPath<TowerData>($"Assets/RushGame/Data/Towers/{_previewTower}.asset");
        }

        /// <summary>
        /// ScriptableObject 값은 플레이 중에 바꿔도 다음 발사부터 바로 반영된다
        /// (Tower가 매 공격마다 Data.Extras를 다시 읽는다).
        /// 플레이 중에는 디스크 저장이 무시되므로 표시만 해 두고, 플레이가 끝날 때 한 번에 쓴다.
        /// </summary>
        void MarkExtrasDirty(TowerData data)
        {
            // UI 구성 중 발생한 콜백은 사용자 조작이 아니므로 무시한다
            if (_buildingExtrasUI)
                return;

            EditorUtility.SetDirty(data);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _extrasPendingSave = true;
                return;
            }

            AssetDatabase.SaveAssetIfDirty(data);
        }

        /// <summary>플레이 중에 조정한 실험 옵션을 플레이 종료 시점에 디스크로 넘긴다.</summary>
        void SavePendingExtras()
        {
            if (!_extrasPendingSave)
                return;

            _extrasPendingSave = false;

            foreach (var name in PreviewTowers)
            {
                var data = AssetDatabase.LoadAssetAtPath<TowerData>($"Assets/RushGame/Data/Towers/{name}.asset");

                if (data == null)
                    continue;

                AssetDatabase.SaveAssetIfDirty(data);
            }

            OnReported("플레이 중 조정한 실험 옵션을 에셋에 저장함");
        }

        static Label MakeSubHeader(string text)
        {
            var label = new Label(text);
            label.style.color = Color.white;
            label.style.fontSize = 11;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8f;
            label.style.marginBottom = 4f;

            return label;
        }

        static void AddToggle(VisualElement parent, string label, bool value, System.Action<bool> onChanged)
        {
            var toggle = new Toggle(label);
            toggle.value = value;
            toggle.style.marginBottom = 2f;
            toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));

            parent.Add(toggle);
        }

        static void AddSlider(VisualElement parent, string label, float value, float min, float max,
            System.Action<float> onChanged)
        {
            var slider = new Slider(label, min, max);
            slider.value = value;
            slider.showInputField = true;
            slider.style.marginBottom = 2f;
            slider.RegisterValueChangedCallback(evt => onChanged(evt.newValue));

            parent.Add(slider);
        }

        static void AddSliderInt(VisualElement parent, string label, int value, int min, int max,
            System.Action<int> onChanged)
        {
            var slider = new SliderInt(label, min, max);
            slider.value = value;
            slider.showInputField = true;
            slider.style.marginBottom = 2f;
            slider.RegisterValueChangedCallback(evt => onChanged(evt.newValue));

            parent.Add(slider);
        }

        void SetPreviewEnabled(bool enabled)
        {
            if (_previewEnabled == enabled)
                return;

            _previewEnabled = enabled;

            if (enabled)
            {
                SceneView.duringSceneGui += OnSceneGui;
                RebuildPreview();
                return;
            }

            SceneView.duringSceneGui -= OnSceneGui;
            _previewPaths.Clear();
            _previewInfo.text = "미리보기 꺼짐";

            SceneView.RepaintAll();
        }

        /// <summary>런타임 Projectile과 같은 MotionTrajectory로 궤적을 뽑아 둔다 (매 리페인트마다 다시 뽑으면 깜빡인다).</summary>
        void RebuildPreview()
        {
            if (!_previewEnabled)
                return;

            _previewPaths.Clear();

            var data = AssetDatabase.LoadAssetAtPath<TowerData>($"Assets/RushGame/Data/Towers/{_previewTower}.asset");

            if (data == null || data.Motion == null)
            {
                _previewInfo.text = $"{_previewTower} 데이터를 찾을 수 없음";
                return;
            }

            if (!TryGetPreviewEndpoints(out var start, out var end))
            {
                _previewInfo.text = "씬에 TowerSlot 또는 PathRoute가 없음 - 씬 셋업 먼저 실행";
                return;
            }

            const int Steps = 40;
            int lines = Mathf.Max(1, data.Motion.ShotCount) * 3;

            for (int line = 0; line < lines; line++)
            {
                var trajectory = new MotionTrajectory();
                trajectory.Sample(data.Motion);

                Vector3 scatteredEnd = end + trajectory.EndScatterOffset;
                var points = new Vector3[Steps + 1];

                for (int i = 0; i <= Steps; i++)
                {
                    float t = (float)i / Steps;
                    points[i] = trajectory.Evaluate(start, scatteredEnd, data.Motion.EvaluateTime(t));
                }

                _previewPaths.Add(points);
            }

            float distance = Vector3.Distance(start, end);
            float flight = distance / Mathf.Max(0.1f, data.ProjectileSpeed);

            _previewInfo.text =
                $"{_previewTower} · {data.Motion.Kind} · {Mathf.Max(1, data.Motion.ShotCount)}발" +
                $"\n비행거리 {distance:0.0} / 비행시간 {flight:0.00}초 · 궤적 {lines}개 표시";

            SceneView.RepaintAll();
        }

        bool TryGetPreviewEndpoints(out Vector3 start, out Vector3 end)
        {
            start = Vector3.zero;
            end = Vector3.zero;

            var slot = GetPreviewSlot();

            if (slot == null)
                return false;

            var routes = FindObjectsByType<PathRoute>(FindObjectsSortMode.None);

            if (routes.Length == 0)
                return false;

            // 타워 총구 높이는 Tower.MuzzlePosition과 같게 맞춘다
            start = slot.BuildPosition + Vector3.up * 1.2f;

            // 표적은 이 슬롯에서 가장 가까운 웨이포인트 (루트 4개 전체에서 고른다)
            Vector3 origin = slot.transform.position;
            float bestSqr = float.MaxValue;
            bool found = false;

            foreach (var route in routes)
            {
                if (route.PointCount < 2)
                    continue;

                for (int i = 0; i < route.PointCount; i++)
                {
                    Vector3 candidate = route.GetPoint(i);
                    float distSqr = (candidate - origin).sqrMagnitude;

                    if (distSqr >= bestSqr)
                        continue;

                    bestSqr = distSqr;
                    end = candidate + Vector3.up * 0.4f;
                    found = true;
                }
            }

            return found;
        }

        static TowerSlot GetPreviewSlot()
        {
            if (Selection.activeGameObject != null)
            {
                var selected = Selection.activeGameObject.GetComponentInParent<TowerSlot>();

                if (selected != null)
                    return selected;
            }

            return FindFirstObjectByType<TowerSlot>();
        }

        void OnSceneGui(SceneView view)
        {
            if (!_previewEnabled)
                return;

            if (_previewPaths.Count == 0)
                return;

            foreach (var points in _previewPaths)
            {
                Handles.color = new Color(0.4f, 0.85f, 1f, 0.9f);
                Handles.DrawAAPolyLine(3f, points);

                Handles.color = new Color(1f, 0.8f, 0.3f, 1f);
                Handles.SphereHandleCap(0, points[points.Length - 1], Quaternion.identity, 0.12f, EventType.Repaint);
            }

            Handles.color = new Color(0.3f, 1f, 0.5f, 1f);
            Handles.SphereHandleCap(0, _previewPaths[0][0], Quaternion.identity, 0.18f, EventType.Repaint);
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
