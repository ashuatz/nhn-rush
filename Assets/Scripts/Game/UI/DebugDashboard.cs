using System.Text;
using Rush.Combat;
using Rush.Stage;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 런타임 디버그 오버레이. GameLog 최근 항목 + 핵심 상태 카운터를 표시한다.
    /// F1으로 토글. 릴리즈 빌드에서는 기본 비표시로 두면 된다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class DebugDashboard : MonoBehaviour
    {
        const int VisibleLogLines = 12;

        static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.65f);

        [SerializeField] StageController _stage;

        /// <summary>기본은 접힘. 패널이 경로 시작 구간을 가리므로 필요할 때만 F1으로 연다.</summary>
        [SerializeField] bool _openOnStart;

        VisualElement _panel;
        VisualElement _hint;
        Label _statsLabel;
        Label _logLabel;
        bool _dirty = true;
        float _fpsAccum;
        int _fpsFrames;
        float _fps;

        void OnEnable()
        {
            BuildUI(GetComponent<UIDocument>().rootVisualElement);

            GameLog.Logged += OnLogged;

            SetVisible(_openOnStart);
        }

        void OnDisable()
        {
            GameLog.Logged -= OnLogged;

            if (_panel != null)
            {
                _panel.RemoveFromHierarchy();
                _panel = null;
            }

            if (_hint != null)
            {
                _hint.RemoveFromHierarchy();
                _hint = null;
            }
        }

        void OnLogged(GameLog.Entry entry)
        {
            _dirty = true;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                SetVisible(_panel.style.display == DisplayStyle.None);

            TickFps();

            if (_panel.style.display == DisplayStyle.None)
                return;

            RefreshStats();

            if (!_dirty)
                return;

            _dirty = false;
            RefreshLog();
        }

        void TickFps()
        {
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;

            if (_fpsAccum < 0.5f)
                return;

            _fps = _fpsFrames / _fpsAccum;
            _fpsAccum = 0f;
            _fpsFrames = 0;
        }

        void BuildUI(VisualElement root)
        {
            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.left = 8;
            _panel.style.bottom = 8;
            _panel.style.width = 420;
            _panel.style.backgroundColor = PanelColor;
            _panel.style.paddingLeft = 10;
            _panel.style.paddingRight = 10;
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;

            _statsLabel = new Label();
            _statsLabel.style.color = new Color(0.6f, 1f, 0.6f, 1f);
            _statsLabel.style.fontSize = 11;
            _statsLabel.style.marginBottom = 4;
            _panel.Add(_statsLabel);

            var verbose = new Toggle("피해 상세 로그");
            verbose.value = GameLog.VerboseCombat;
            verbose.style.fontSize = 11;
            verbose.style.color = Color.white;
            verbose.RegisterValueChangedCallback(evt => GameLog.VerboseCombat = evt.newValue);
            _panel.Add(verbose);

            _logLabel = new Label();
            _logLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            _logLabel.style.fontSize = 11;
            _logLabel.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(_logLabel);

            // 화면 앵커 UI라 레터박스 안쪽 칸에 넣는다
            var content = UiLayers.Content(root);
            content.Add(_panel);

            // 패널이 접혀 있어도 여는 법을 알 수 있게 작은 안내를 남긴다
            _hint = new Label("[F1] 디버그");
            _hint.style.position = Position.Absolute;
            _hint.style.left = 8;
            _hint.style.bottom = 8;
            _hint.style.fontSize = 10;
            _hint.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            _hint.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            _hint.style.paddingLeft = 6;
            _hint.style.paddingRight = 6;
            _hint.style.paddingTop = 2;
            _hint.style.paddingBottom = 2;

            content.Add(_hint);
        }

        void SetVisible(bool visible)
        {
            if (_panel == null)
                return;

            if (visible)
            {
                _panel.style.display = DisplayStyle.Flex;
                _hint.style.display = DisplayStyle.None;
                _dirty = true;
                return;
            }

            _panel.style.display = DisplayStyle.None;
            _hint.style.display = DisplayStyle.Flex;
        }

        void RefreshStats()
        {
            if (_stage == null)
            {
                _statsLabel.text = "StageController 참조 없음";
                return;
            }

            _statsLabel.text =
                $"[F1] {_stage.Phase} | 웨이브 {_stage.WaveNumber}/{_stage.TotalWaves}" +
                $" | 골드 {_stage.Gold} | 생명 {_stage.Life}" +
                $" | 몬스터 {MonsterRegistry.Active.Count} | 병사 {Soldier.Active.Count}" +
                $" | {_fps:F0} FPS";
        }

        void RefreshLog()
        {
            var entries = GameLog.Entries;
            var sb = new StringBuilder();

            int start = Mathf.Max(0, entries.Count - VisibleLogLines);

            for (int i = start; i < entries.Count; i++)
            {
                var entry = entries[i];
                sb.AppendLine($"[{entry.Time,6:F1}] [{entry.Category}] {entry.Message}");
            }

            _logLabel.text = sb.ToString();
        }
    }
}
