using Rush.Stage;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 세로(좁은) 화면 안내막. HUD가 가로 화면 기준으로 상하좌우에 붙어 있어
    /// 세로에서는 서로 겹치고 목록이 잘린다. 세로 레이아웃을 따로 만드는 대신
    /// 가로로 돌려/넓혀 달라고 막고, 막혀 있는 동안 게임을 멈춘다.
    ///
    /// UIDocument를 GameHUD 등과 공유하므로 표시할 때마다 맨 앞으로 올린다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class OrientationGate : MonoBehaviour
    {
        static readonly Color DimColor = new Color(0.02f, 0.02f, 0.04f, 0.96f);
        static readonly Color AccentColor = new Color(1f, 0.86f, 0.45f, 1f);

        /// <summary>이 가로세로비 미만이면 안내막을 띄운다. 4:3이 1.33이라 그보다 좁을 때만 걸린다.</summary>
        [SerializeField] float _minAspect = 1.2f;

        [SerializeField] StageController _stage;

        VisualElement _gate;
        Label _sizeLabel;
        bool _blocking;

        // 해상도 라벨에 이미 찍어둔 값. 매 프레임 같은 문자열을 새로 만들지 않기 위해 들고 있는다.
        int _shownWidth;
        int _shownHeight;

        void OnEnable()
        {
            BuildUI(GetComponent<UIDocument>().rootVisualElement);

            Apply(IsNarrow());
        }

        void OnDisable()
        {
            // 안내막이 사라지는데 정지가 남으면 게임이 멈춘 채 조작만 풀린다
            if (_blocking && _stage != null)
                _stage.SetAspectPause(false);

            _blocking = false;

            if (_gate != null)
            {
                _gate.RemoveFromHierarchy();
                _gate = null;
            }
        }

        // timeScale이 0이어도 Update는 계속 돌아 스스로 안내막을 걷을 수 있다
        void Update()
        {
            Apply(IsNarrow());
        }

        bool IsNarrow()
        {
            // 폭이 0인 프레임(최소화 등)에서는 비율이 0이 되어 세로로 오판한다. 둘 다 확인한다.
            if (Screen.width <= 0 || Screen.height <= 0)
                return false;

            return (float)Screen.width / Screen.height < _minAspect;
        }

        void Apply(bool narrow)
        {
            if (narrow)
            {
                RefreshSizeLabel();
                KeepOnTop();
            }

            if (narrow == _blocking)
                return;

            _blocking = narrow;

            if (narrow)
            {
                _gate.style.display = DisplayStyle.Flex;
                _gate.BringToFront();

                // 안내막이 뜨기 전에 포커스를 잡고 있던 버튼이 키보드 입력을 계속 먹는 것을 막는다
                _gate.Focus();
            }
            else
            {
                _gate.style.display = DisplayStyle.None;
            }

            if (_stage != null)
                _stage.SetAspectPause(narrow);
        }

        /// <summary>
        /// 문서를 HUD와 공유하므로 다른 컴포넌트가 뒤늦게 요소를 얹으면 안내막이 그 아래로 깔린다.
        /// 맨 앞이 아닐 때만 다시 올린다 - 매 프레임 순서를 건드리면 쓸데없는 리페인트가 생긴다.
        /// </summary>
        void KeepOnTop()
        {
            var parent = _gate.parent;

            if (parent == null)
                return;

            if (parent[parent.childCount - 1] == _gate)
                return;

            _gate.BringToFront();
        }

        /// <summary>해상도가 실제로 바뀔 때만 갱신한다. 안내막은 떠 있는 내내 매 프레임 Apply를 지나간다.</summary>
        void RefreshSizeLabel()
        {
            if (Screen.width == _shownWidth && Screen.height == _shownHeight)
                return;

            _shownWidth = Screen.width;
            _shownHeight = Screen.height;

            _sizeLabel.text = $"현재 {_shownWidth} x {_shownHeight}";
        }

        void BuildUI(VisualElement root)
        {
            _gate = new VisualElement();
            _gate.style.position = Position.Absolute;
            _gate.style.left = 0;
            _gate.style.right = 0;
            _gate.style.top = 0;
            _gate.style.bottom = 0;
            _gate.style.backgroundColor = DimColor;
            _gate.style.alignItems = Align.Center;
            _gate.style.justifyContent = Justify.Center;
            _gate.style.display = DisplayStyle.None;

            // pickingMode는 기본값(Position)으로 둔다. 뒤쪽 HUD 클릭과
            // 월드 클릭(BuildMenu가 panel.Pick으로 UI 위를 걸러낸다)을 둘 다 막아야 한다.
            //
            // 포커스도 받을 수 있어야 한다. 안 그러면 표시 시점에 포커스를 빼앗아 오지 못한다.
            _gate.focusable = true;

            // 재활성화될 때 라벨은 새로 만들어지므로 캐시도 같이 비운다 (안 비우면 빈 라벨로 남는다)
            _shownWidth = 0;
            _shownHeight = 0;

            // 가로로 눕힌 빈 액자. "이 모양으로 맞춰 달라"를 글자보다 빠르게 읽히게 한다.
            var frame = new VisualElement();
            frame.style.width = 160;
            frame.style.height = 90;
            frame.style.borderLeftWidth = 3;
            frame.style.borderRightWidth = 3;
            frame.style.borderTopWidth = 3;
            frame.style.borderBottomWidth = 3;
            frame.style.borderLeftColor = AccentColor;
            frame.style.borderRightColor = AccentColor;
            frame.style.borderTopColor = AccentColor;
            frame.style.borderBottomColor = AccentColor;
            frame.style.borderTopLeftRadius = 8;
            frame.style.borderTopRightRadius = 8;
            frame.style.borderBottomLeftRadius = 8;
            frame.style.borderBottomRightRadius = 8;
            frame.style.marginBottom = 22;
            _gate.Add(frame);

            var title = new Label("화면을 가로로");
            title.style.color = Color.white;
            title.style.fontSize = 26;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            _gate.Add(title);

            var body = new Label("이 게임은 가로 화면에 맞춰져 있습니다.\n기기를 돌리거나 창을 가로로 넓혀 주세요.");
            body.style.color = new Color(0.82f, 0.82f, 0.86f, 1f);
            body.style.fontSize = 14;
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.unityTextAlign = TextAnchor.MiddleCenter;
            _gate.Add(body);

            // 실제 해상도를 같이 띄운다. 안내막이 왜 떴는지 바로 확인된다.
            _sizeLabel = new Label();
            _sizeLabel.style.color = new Color(0.55f, 0.55f, 0.6f, 1f);
            _sizeLabel.style.fontSize = 11;
            _sizeLabel.style.marginTop = 14;
            _gate.Add(_sizeLabel);

            root.Add(_gate);
        }
    }
}
