using System.Collections.Generic;
using Rush.Stage;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 상단 자원 표시 + 조기소환 버튼 + 승패 오버레이. UI Toolkit 코드 구성.
    /// StageController.Changed 이벤트를 구독만 하고, 게임 상태를 직접 바꾸지 않는다
    /// (조기소환/재시작 버튼은 StageController의 공개 API 호출).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameHUD : MonoBehaviour
    {
        static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.1f, 0.85f);
        static readonly Color TextColor = Color.white;
        static readonly Color PauseBorderColor = new Color(1f, 0.92f, 0.6f, 0.75f);
        static readonly Color FlagColor = new Color(0.5f, 0.12f, 0.12f, 0.92f);
        static readonly Color FlagAccentColor = new Color(1f, 0.78f, 0.32f, 1f);

        const float PauseButtonSize = 38f;

        /// <summary>입구 표기 깃발 크기와 스폰 지점에서 띄울 높이 (패널 픽셀).</summary>
        const float FlagWidth = 128f;
        const float FlagLift = 16f;
        const float FlagFallbackHeight = 44f;

        [SerializeField] StageController _stage;
        [SerializeField] Rush.Combat.MonsterDebugView _debugView;

        /// <summary>입구 표기를 화면 좌표로 옮길 때 쓰는 카메라. 비면 Camera.main을 쓴다.</summary>
        [SerializeField] Camera _worldCamera;

        /// <summary>다음 웨이브 입구에 뜨는 깃발 하나.</summary>
        class EntranceFlag
        {
            public VisualElement Root;
            public Label Title;
            public Label Bonus;
        }

        readonly List<EntranceFlag> _flags = new List<EntranceFlag>(4);

        /// <summary>입구 깃발이 떠 있는지. 떠 있으면 우하단 조기소환 버튼은 감춘다 (중복 UI 방지).</summary>
        bool _flagsVisible;

        VisualElement _flagLayer;
        VisualElement _container;
        Label _goldLabel;
        Label _lifeLabel;
        Label _waveLabel;
        Label _countdownLabel;
        Button _earlyCallButton;
        Button _speedButton;
        Button _hpToggleButton;
        Button _pauseButton;
        VisualElement _pauseOverlay;
        VisualElement _resultOverlay;
        Label _resultLabel;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;

            BuildUI(root);

            if (_stage != null)
                _stage.Changed += Refresh;

            Refresh();
        }

        void OnDisable()
        {
            if (_stage != null)
                _stage.Changed -= Refresh;

            // 깃발은 컨테이너와 함께 사라지므로 참조도 같이 버린다 (다시 켜질 때 새로 만든다)
            _flags.Clear();
            _flagLayer = null;
            _flagsVisible = false;

            if (_container != null)
            {
                _container.RemoveFromHierarchy();
                _container = null;
            }
        }

        void Update()
        {
            if (_stage == null)
                return;

            // 입구 깃발은 카메라가 움직이면 따라가야 하므로 매 프레임 위치를 다시 잡는다.
            // 카운트다운 패널이 깃발 유무를 보고 조기소환 버튼을 숨기므로 먼저 갱신한다.
            RefreshEntranceFlags();

            // 카운트다운은 매 프레임 갱신 (Changed 이벤트 대상이 아님)
            RefreshCountdown();

            // HP 토글은 외부(디버그 등)에서 바뀔 수 있어 함께 동기화한다
            RefreshHpToggle();
        }

        void BuildUI(VisualElement root)
        {
            _container = new VisualElement();
            _container.pickingMode = PickingMode.Ignore;
            _container.style.position = Position.Absolute;
            _container.style.left = 0;
            _container.style.right = 0;
            _container.style.top = 0;
            _container.style.bottom = 0;

            // 입구 깃발은 월드 좌표를 따라다니므로 다른 패널보다 아래에 깔아 둔다
            _flagLayer = new VisualElement();
            _flagLayer.pickingMode = PickingMode.Ignore;
            _flagLayer.style.position = Position.Absolute;
            _flagLayer.style.left = 0;
            _flagLayer.style.right = 0;
            _flagLayer.style.top = 0;
            _flagLayer.style.bottom = 0;
            _container.Add(_flagLayer);

            _container.Add(BuildTopBar());
            _container.Add(BuildPauseButton());
            _container.Add(BuildWavePanel());
            _container.Add(BuildResultOverlay());

            // 일시정지 팝업은 다른 UI를 덮어야 하므로 마지막에 넣는다
            _container.Add(BuildPauseOverlay());

            root.Add(_container);
        }

        VisualElement BuildTopBar()
        {
            var bar = new VisualElement();
            bar.pickingMode = PickingMode.Ignore;
            bar.style.position = Position.Absolute;
            bar.style.top = 8;
            bar.style.left = 8;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.backgroundColor = PanelColor;
            bar.style.paddingLeft = 14;
            bar.style.paddingRight = 14;
            bar.style.paddingTop = 8;
            bar.style.paddingBottom = 8;
            bar.style.borderTopLeftRadius = 6;
            bar.style.borderTopRightRadius = 6;
            bar.style.borderBottomLeftRadius = 6;
            bar.style.borderBottomRightRadius = 6;

            _goldLabel = MakeStatLabel();
            _lifeLabel = MakeStatLabel();
            _waveLabel = MakeStatLabel();

            bar.Add(_goldLabel);
            bar.Add(_lifeLabel);
            bar.Add(_waveLabel);

            _speedButton = new Button(OnSpeedClicked);
            _speedButton.style.fontSize = 13;
            _speedButton.style.marginTop = 0;
            _speedButton.style.marginBottom = 0;
            _speedButton.style.paddingLeft = 10;
            _speedButton.style.paddingRight = 10;
            bar.Add(_speedButton);

            _hpToggleButton = new Button(OnHpToggleClicked);
            _hpToggleButton.style.fontSize = 13;
            _hpToggleButton.style.marginTop = 0;
            _hpToggleButton.style.marginBottom = 0;
            _hpToggleButton.style.marginLeft = 6;
            _hpToggleButton.style.paddingLeft = 10;
            _hpToggleButton.style.paddingRight = 10;
            bar.Add(_hpToggleButton);

            return bar;
        }

        VisualElement BuildPauseButton()
        {
            _pauseButton = new Button(OnPauseClicked);
            _pauseButton.text = string.Empty;
            _pauseButton.tooltip = "일시정지";

            _pauseButton.style.position = Position.Absolute;
            _pauseButton.style.top = 8;
            _pauseButton.style.right = 8;
            _pauseButton.style.width = PauseButtonSize;
            _pauseButton.style.height = PauseButtonSize;
            _pauseButton.style.backgroundColor = PanelColor;
            _pauseButton.style.alignItems = Align.Center;
            _pauseButton.style.justifyContent = Justify.Center;

            ClearSpacing(_pauseButton);

            // 건설 라디얼의 아이콘 버튼과 같은 원형 + 금색 테두리로 맞춘다
            _pauseButton.style.borderLeftWidth = 2;
            _pauseButton.style.borderRightWidth = 2;
            _pauseButton.style.borderTopWidth = 2;
            _pauseButton.style.borderBottomWidth = 2;
            _pauseButton.style.borderLeftColor = PauseBorderColor;
            _pauseButton.style.borderRightColor = PauseBorderColor;
            _pauseButton.style.borderTopColor = PauseBorderColor;
            _pauseButton.style.borderBottomColor = PauseBorderColor;

            float radius = PauseButtonSize * 0.5f;
            _pauseButton.style.borderTopLeftRadius = radius;
            _pauseButton.style.borderTopRightRadius = radius;
            _pauseButton.style.borderBottomLeftRadius = radius;
            _pauseButton.style.borderBottomRightRadius = radius;

            var icon = new GlyphIcon(IconGlyph.Pause);
            icon.style.width = PauseButtonSize * 0.4f;
            icon.style.height = PauseButtonSize * 0.4f;
            icon.style.flexShrink = 0f;
            _pauseButton.Add(icon);

            return _pauseButton;
        }

        /// <summary>기본 테마가 넣는 마진/패딩을 걷어낸다. 남아 있으면 원형 버튼 안에서 아이콘이 밀린다.</summary>
        static void ClearSpacing(VisualElement element)
        {
            element.style.marginLeft = 0;
            element.style.marginRight = 0;
            element.style.marginTop = 0;
            element.style.marginBottom = 0;
            element.style.paddingLeft = 0;
            element.style.paddingRight = 0;
            element.style.paddingTop = 0;
            element.style.paddingBottom = 0;
        }

        VisualElement BuildPauseOverlay()
        {
            _pauseOverlay = new VisualElement();
            _pauseOverlay.style.position = Position.Absolute;
            _pauseOverlay.style.left = 0;
            _pauseOverlay.style.right = 0;
            _pauseOverlay.style.top = 0;
            _pauseOverlay.style.bottom = 0;
            _pauseOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            _pauseOverlay.style.alignItems = Align.Center;
            _pauseOverlay.style.justifyContent = Justify.Center;
            _pauseOverlay.style.display = DisplayStyle.None;

            var box = new VisualElement();
            box.style.backgroundColor = PanelColor;
            box.style.minWidth = 220;
            box.style.paddingLeft = 28;
            box.style.paddingRight = 28;
            box.style.paddingTop = 22;
            box.style.paddingBottom = 22;
            box.style.borderTopLeftRadius = 8;
            box.style.borderTopRightRadius = 8;
            box.style.borderBottomLeftRadius = 8;
            box.style.borderBottomRightRadius = 8;
            box.style.alignItems = Align.Stretch;

            var title = new Label("일시정지");
            title.style.color = TextColor;
            title.style.fontSize = 26;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 16;
            box.Add(title);

            box.Add(MakeMenuButton("계속하기", OnResumeClicked));
            box.Add(MakeMenuButton("다시 시작", OnRestartClicked));
            box.Add(MakeMenuButton("종료", OnQuitClicked));

            _pauseOverlay.Add(box);

            return _pauseOverlay;
        }

        static Button MakeMenuButton(string text, System.Action onClick)
        {
            var button = new Button(onClick);
            button.text = text;
            button.style.fontSize = 15;
            button.style.marginLeft = 0;
            button.style.marginRight = 0;
            button.style.marginBottom = 6;
            button.style.paddingTop = 8;
            button.style.paddingBottom = 8;

            return button;
        }

        VisualElement BuildWavePanel()
        {
            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.bottom = 12;
            panel.style.right = 12;
            panel.style.backgroundColor = PanelColor;
            panel.style.paddingLeft = 12;
            panel.style.paddingRight = 12;
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 10;
            panel.style.borderTopLeftRadius = 6;
            panel.style.borderTopRightRadius = 6;
            panel.style.borderBottomLeftRadius = 6;
            panel.style.borderBottomRightRadius = 6;
            panel.style.alignItems = Align.Stretch;

            _countdownLabel = new Label();
            _countdownLabel.style.color = TextColor;
            _countdownLabel.style.fontSize = 13;
            _countdownLabel.style.marginBottom = 6;
            panel.Add(_countdownLabel);

            _earlyCallButton = new Button(OnEarlyCallClicked);
            _earlyCallButton.style.fontSize = 13;
            panel.Add(_earlyCallButton);

            return panel;
        }

        VisualElement BuildResultOverlay()
        {
            _resultOverlay = new VisualElement();
            _resultOverlay.style.position = Position.Absolute;
            _resultOverlay.style.left = 0;
            _resultOverlay.style.right = 0;
            _resultOverlay.style.top = 0;
            _resultOverlay.style.bottom = 0;
            _resultOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            _resultOverlay.style.alignItems = Align.Center;
            _resultOverlay.style.justifyContent = Justify.Center;
            _resultOverlay.style.display = DisplayStyle.None;

            _resultLabel = new Label();
            _resultLabel.style.color = TextColor;
            _resultLabel.style.fontSize = 42;
            _resultLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _resultLabel.style.marginBottom = 18;
            _resultOverlay.Add(_resultLabel);

            var restart = new Button(OnRestartClicked);
            restart.text = "다시 시작";
            restart.style.fontSize = 16;
            restart.style.paddingLeft = 24;
            restart.style.paddingRight = 24;
            restart.style.paddingTop = 8;
            restart.style.paddingBottom = 8;
            _resultOverlay.Add(restart);

            return _resultOverlay;
        }

        Label MakeStatLabel()
        {
            var label = new Label();
            label.style.color = TextColor;
            label.style.fontSize = 15;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginRight = 18;

            return label;
        }

        void OnEarlyCallClicked()
        {
            if (_stage == null)
                return;

            _stage.CallNextWaveEarly();
        }

        // ---------- 입구 표기 ----------

        Camera ResolveCamera()
        {
            if (_worldCamera != null)
                return _worldCamera;

            _worldCamera = Camera.main;

            return _worldCamera;
        }

        EntranceFlag GetFlag(int index)
        {
            while (_flags.Count <= index)
                _flags.Add(BuildFlag());

            return _flags[index];
        }

        EntranceFlag BuildFlag()
        {
            var root = new VisualElement();
            root.style.position = Position.Absolute;
            root.style.width = FlagWidth;
            root.style.backgroundColor = FlagColor;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 5;
            root.style.paddingBottom = 5;
            root.style.borderTopLeftRadius = 5;
            root.style.borderTopRightRadius = 5;
            root.style.borderBottomLeftRadius = 5;
            root.style.borderBottomRightRadius = 5;
            root.style.borderBottomWidth = 2;
            root.style.borderBottomColor = FlagAccentColor;
            root.style.alignItems = Align.Center;
            root.style.display = DisplayStyle.None;

            var title = new Label();
            title.style.color = TextColor;
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(title);

            var bonus = new Label();
            bonus.style.color = FlagAccentColor;
            bonus.style.fontSize = 11;
            root.Add(bonus);

            root.RegisterCallback<ClickEvent>(evt => OnEarlyCallClicked());

            _flagLayer.Add(root);

            return new EntranceFlag { Root = root, Title = title, Bonus = bonus };
        }

        void HideFlagsFrom(int index)
        {
            for (int i = index; i < _flags.Count; i++)
                _flags[i].Root.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 다음 웨이브가 나올 입구에 깃발을 띄운다. 깃발을 누르면 조기소환이고,
        /// 보너스는 남은 시간에 비례하므로 카운트다운과 함께 줄어든다.
        /// </summary>
        void RefreshEntranceFlags()
        {
            if (_flagLayer == null)
                return;

            var entrances = _stage.NextWaveEntrances;
            var camera = ResolveCamera();

            if (camera == null || entrances == null || entrances.Count == 0 || !_stage.CanCallEarly)
            {
                HideFlagsFrom(0);
                _flagsVisible = false;
                return;
            }

            var panel = _flagLayer.panel;

            if (panel == null)
                return;

            string title = $"웨이브 {_stage.WaveNumber + 1} 진입 {Mathf.CeilToInt(_stage.NextWaveIn)}초";
            string bonus = $"조기소환 +{_stage.EarlyCallBonus}G";

            int shown = 0;

            for (int i = 0; i < entrances.Count; i++)
            {
                var route = entrances[i];

                if (route == null || route.PointCount < 1)
                    continue;

                var world = route.GetPoint(0);

                // 카메라 뒤로 넘어간 입구는 화면에 그리지 않는다
                if (camera.WorldToViewportPoint(world).z <= 0f)
                    continue;

                var flag = GetFlag(shown);
                var position = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, camera);

                // 첫 프레임에는 레이아웃이 없어 높이가 0으로 읽힌다. 그때는 기본 높이로 앉힌다.
                float height = flag.Root.resolvedStyle.height;

                if (height <= 0f)
                    height = FlagFallbackHeight;

                flag.Root.style.left = position.x - FlagWidth * 0.5f;
                flag.Root.style.top = position.y - height - FlagLift;
                flag.Root.style.display = DisplayStyle.Flex;
                flag.Title.text = title;
                flag.Bonus.text = bonus;

                shown++;
            }

            HideFlagsFrom(shown);

            _flagsVisible = shown > 0;
        }

        void OnSpeedClicked()
        {
            if (_stage == null)
                return;

            _stage.CycleSpeed();
        }

        void OnHpToggleClicked()
        {
            if (_debugView == null)
                return;

            _debugView.DisplayEnabled = !_debugView.DisplayEnabled;

            RefreshHpToggle();
        }

        void RefreshHpToggle()
        {
            if (_hpToggleButton == null)
                return;

            // 구간 머티리얼이 안 꽂혀 있으면 눌러도 아무 일도 안 일어난다. 그럴 땐 버튼을 감춘다.
            if (_debugView == null || !_debugView.IsReady)
            {
                _hpToggleButton.style.display = DisplayStyle.None;
                return;
            }

            _hpToggleButton.style.display = DisplayStyle.Flex;
            _hpToggleButton.text = _debugView.DisplayEnabled ? "HP 디버그 끄기" : "HP 디버그 켜기";
        }

        void OnPauseClicked()
        {
            SetPaused(true);
        }

        void OnResumeClicked()
        {
            SetPaused(false);
        }

        /// <summary>게임 정지 자체는 StageController가 담당하고, HUD는 팝업 표시만 맡는다.</summary>
        void SetPaused(bool paused)
        {
            if (_stage == null || _pauseOverlay == null)
                return;

            _stage.SetMenuPause(paused);

            _pauseOverlay.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;

            // 건설 라디얼 등과 UIDocument를 공유하므로, 앞으로 끌어와야 뒤쪽 클릭이 막힌다
            if (paused)
                _pauseOverlay.BringToFront();
        }

        void OnRestartClicked()
        {
            if (_stage == null)
                return;

            // timeScale은 씬을 새로 불러도 남으므로 먼저 되돌린다
            SetPaused(false);

            _stage.RestartStage();
        }

        void OnQuitClicked()
        {
            SetPaused(false);

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Refresh()
        {
            if (_stage == null || _container == null)
                return;

            _goldLabel.text = $"골드 {_stage.Gold}";
            _lifeLabel.text = $"생명 {_stage.Life}";
            _waveLabel.text = $"웨이브 {_stage.WaveNumber}/{_stage.TotalWaves} ({_stage.DifficultyName})";
            _speedButton.text = $"배속 {_stage.CurrentSpeed:0.#}x";
            RefreshHpToggle();

            RefreshCountdown();
            RefreshResult();
        }

        void RefreshCountdown()
        {
            if (_countdownLabel == null)
                return;

            // "없음" 표기는 정말 웨이브가 다 나왔을 때만. 보상 선택 등으로 잠시 막힌 상태와 구분한다.
            if (_stage.AllWavesStarted)
            {
                _countdownLabel.text = "남은 웨이브 없음";
                _earlyCallButton.style.display = DisplayStyle.None;
                return;
            }

            // 보스를 처치할 때까지 카운트다운이 멈춘다. 숫자가 굳은 이유를 알려준다.
            if (_stage.BossGateBlocking)
            {
                _countdownLabel.text = "보스를 처치해야 다음 웨이브로 넘어간다";
                _earlyCallButton.style.display = DisplayStyle.None;
                return;
            }

            _countdownLabel.text = $"다음 웨이브까지 {Mathf.CeilToInt(_stage.NextWaveIn)}초";

            // 입구 깃발이 떠 있으면 조기소환은 그쪽에서 한다
            if (!_stage.CanCallEarly || _flagsVisible)
            {
                _earlyCallButton.style.display = DisplayStyle.None;
                return;
            }

            _earlyCallButton.style.display = DisplayStyle.Flex;
            _earlyCallButton.text = $"조기소환 (+{_stage.EarlyCallBonus}G)";
        }

        void RefreshResult()
        {
            bool finished = _stage.Phase == StagePhase.Victory || _stage.Phase == StagePhase.Defeat;

            // 승패가 갈리면 일시정지는 의미가 없다. 열려 있었다면 닫고 버튼도 숨긴다.
            if (finished && _stage.MenuPauseActive)
                SetPaused(false);

            if (_pauseButton != null)
                _pauseButton.style.display = finished ? DisplayStyle.None : DisplayStyle.Flex;

            if (_stage.Phase == StagePhase.Victory)
            {
                _resultOverlay.style.display = DisplayStyle.Flex;
                _resultLabel.text = "승리";
                return;
            }

            if (_stage.Phase == StagePhase.Defeat)
            {
                _resultOverlay.style.display = DisplayStyle.Flex;
                _resultLabel.text = "패배";
                return;
            }

            _resultOverlay.style.display = DisplayStyle.None;
        }
    }
}
