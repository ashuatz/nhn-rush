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

        [SerializeField] StageController _stage;

        VisualElement _container;
        Label _goldLabel;
        Label _lifeLabel;
        Label _waveLabel;
        Label _countdownLabel;
        Button _earlyCallButton;
        Button _speedButton;
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

            // 카운트다운은 매 프레임 갱신 (Changed 이벤트 대상이 아님)
            RefreshCountdown();
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

            _container.Add(BuildTopBar());
            _container.Add(BuildWavePanel());
            _container.Add(BuildResultOverlay());

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

            return bar;
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

        void OnSpeedClicked()
        {
            if (_stage == null)
                return;

            _stage.CycleSpeed();
        }

        void OnRestartClicked()
        {
            if (_stage == null)
                return;

            _stage.RestartStage();
        }

        void Refresh()
        {
            if (_stage == null || _container == null)
                return;

            _goldLabel.text = $"골드 {_stage.Gold}";
            _lifeLabel.text = $"생명 {_stage.Life}";
            _waveLabel.text = $"웨이브 {_stage.WaveNumber}/{_stage.TotalWaves} ({_stage.DifficultyName})";
            _speedButton.text = $"배속 {_stage.CurrentSpeed:0.#}x";

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

            _countdownLabel.text = $"다음 웨이브까지 {Mathf.CeilToInt(_stage.NextWaveIn)}초";

            if (!_stage.CanCallEarly)
            {
                _earlyCallButton.style.display = DisplayStyle.None;
                return;
            }

            _earlyCallButton.style.display = DisplayStyle.Flex;
            _earlyCallButton.text = $"조기소환 (+{_stage.EarlyCallBonus}G)";
        }

        void RefreshResult()
        {
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
