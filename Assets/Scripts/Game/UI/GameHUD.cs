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

        // 강조색 (배속 선택 칸, 조기소환 버튼, 카운트다운 바가 공유한다)
        static readonly Color AccentColor = new Color(1f, 0.86f, 0.45f, 1f);
        static readonly Color AccentTextColor = new Color(0.10f, 0.09f, 0.06f, 1f);
        static readonly Color TrackColor = new Color(0.18f, 0.18f, 0.22f, 1f);
        static readonly Color MutedTextColor = new Color(0.66f, 0.66f, 0.72f, 1f);

        /// <summary>남은 시간이 이 비율 아래로 떨어지면 바가 붉게 바뀐다.</summary>
        const float CountdownUrgentRatio = 0.25f;

        static readonly Color UrgentColor = new Color(0.95f, 0.35f, 0.28f, 1f);
        static readonly Color LifeColor = new Color(0.93f, 0.36f, 0.40f, 1f);

        const float StatIconSize = 17f;

        /// <summary>생명이 시작값의 이 비율 아래로 떨어지면 숫자가 붉어진다.</summary>
        const float LifeWarnRatio = 0.34f;

        const float SpeedSegmentWidth = 46f;
        const float SpeedSegmentHeight = 30f;
        const float SpeedPillRadius = 10f;
        static readonly Color FlagColor = new Color(0.5f, 0.12f, 0.12f, 0.92f);
        static readonly Color FlagAccentColor = new Color(1f, 0.78f, 0.32f, 1f);

        const float PauseButtonSize = 38f;

        /// <summary>입구 표기 깃발 크기와 스폰 지점에서 띄울 높이 (패널 픽셀).</summary>
        const float FlagWidth = 168f;
        const float FlagLift = 16f;
        const float FlagFallbackHeight = 44f;

        /// <summary>깃발이 화면 경계에 딱 붙지 않도록 두는 여백.</summary>
        const float FlagMargin = 8f;

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

        VisualElement _flagLayer;
        VisualElement _container;
        Label _goldLabel;
        Label _lifeLabel;
        Label _waveLabel;
        Label _countdownLabel;
        Label _countdownValueLabel;
        VisualElement _countdownTrack;
        VisualElement _countdownFill;
        Button _earlyCallButton;
        readonly List<Button> _speedSegments = new List<Button>(3);
        Button _hpToggleButton;
        GlyphIcon _hpToggleIcon;
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

            _flags.Clear();
            _speedSegments.Clear();

            // 깃발 레이어는 컨테이너 밖(문서 루트)에 있으므로 따로 걷어내야 한다
            if (_flagLayer != null)
            {
                _flagLayer.RemoveFromHierarchy();
                _flagLayer = null;
            }

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

            // 입구 깃발은 카메라가 움직이면 따라가야 하므로 매 프레임 위치를 다시 잡는다
            RefreshEntranceFlags();

            // 카운트다운은 매 프레임 갱신 (Changed 이벤트 대상이 아님)
            RefreshCountdown();

            // HP 토글은 외부(디버그 등)에서 바뀔 수 있어 함께 동기화한다
            RefreshHpToggle();
        }

        void BuildUI(VisualElement root)
        {
            var content = UiLayers.Content(root);

            _container = new VisualElement();
            _container.pickingMode = PickingMode.Ignore;
            _container.style.position = Position.Absolute;
            _container.style.left = 0;
            _container.style.right = 0;
            _container.style.top = 0;
            _container.style.bottom = 0;

            // 입구 깃발은 월드 좌표를 따라다니므로 화면 앵커 칸이 아니라 문서 루트에 둔다.
            // 레터박스로 좁혀진 칸에 넣으면 camera.rect가 이미 반영된 좌표가 한 번 더 밀린다.
            // 다른 패널보다 먼저 넣어 아래에 깔린다.
            _flagLayer = new VisualElement();
            _flagLayer.pickingMode = PickingMode.Ignore;
            _flagLayer.style.position = Position.Absolute;
            _flagLayer.style.left = 0;
            _flagLayer.style.right = 0;
            _flagLayer.style.top = 0;
            _flagLayer.style.bottom = 0;
            root.Add(_flagLayer);

            // 추가 순서에 기대지 않고 깃발을 화면 앵커 칸 바로 아래로 명시적으로 내린다.
            // 칸은 이 컴포넌트가 꺼져도 남아 있어, 껐다 켜면 깃발이 칸 뒤에 추가되며 위로 올라와 버린다.
            _flagLayer.PlaceBehind(content);

            _container.Add(BuildTopBar());
            _container.Add(BuildPauseButton());
            _container.Add(BuildDebugToggle());
            _container.Add(BuildSpeedPanel());
            _container.Add(BuildWavePanel());
            _container.Add(BuildResultOverlay());

            // 일시정지 팝업은 다른 UI를 덮어야 하므로 마지막에 넣는다
            _container.Add(BuildPauseOverlay());

            // 화면 앵커 UI라 레터박스 안쪽 칸에 넣는다. 초광폭 창에서 띠 위로 새지 않게.
            content.Add(_container);
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

            // 값 셋이 전부 같은 흰 굵은 글씨면 훑을 때 구분이 안 된다.
            // 아이콘과 색으로 갈라놓고 웨이브만 텍스트로 둔다.
            bar.Add(MakeStatItem(IconGlyph.Coin, AccentColor, out _goldLabel));
            bar.Add(MakeDivider());
            bar.Add(MakeStatItem(IconGlyph.Heart, LifeColor, out _lifeLabel));
            bar.Add(MakeDivider());

            _waveLabel = new Label();
            _waveLabel.style.color = TextColor;
            _waveLabel.style.fontSize = 16;
            _waveLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _waveLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            bar.Add(_waveLabel);

            return bar;
        }

        /// <summary>아이콘 + 값 한 쌍. 자원 표시의 기본 단위.</summary>
        VisualElement MakeStatItem(IconGlyph glyph, Color iconColor, out Label value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var icon = new GlyphIcon(glyph);
            icon.Tint = iconColor;
            icon.style.width = StatIconSize;
            icon.style.height = StatIconSize;
            icon.style.flexShrink = 0f;
            icon.style.marginRight = 7;
            row.Add(icon);

            value = new Label();
            value.style.color = TextColor;
            value.style.fontSize = 16;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            value.style.unityTextAlign = TextAnchor.MiddleLeft;
            value.style.minWidth = 34;
            row.Add(value);

            return row;
        }

        static VisualElement MakeDivider()
        {
            var divider = new VisualElement();
            divider.style.width = 1;
            divider.style.height = 18;
            divider.style.marginLeft = 14;
            divider.style.marginRight = 14;
            divider.style.backgroundColor = TrackColor;

            return divider;
        }

        /// <summary>
        /// 배속 세그먼트 컨트롤. 좌하단에 두고 우하단 웨이브 패널과 좌우로 마주보게 한다.
        /// 순환 버튼 하나였을 때는 4x에서 1x로 돌아오려면 두 번 눌러야 했고
        /// 고를 수 있는 단계가 몇 개인지도 보이지 않았다. 칸을 다 펼쳐 한 번에 가게 한다.
        /// </summary>
        VisualElement BuildSpeedPanel()
        {
            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.bottom = 12;
            panel.style.left = 12;
            panel.style.flexDirection = FlexDirection.Row;
            panel.style.backgroundColor = PanelColor;
            panel.style.paddingLeft = 4;
            panel.style.paddingRight = 4;
            panel.style.paddingTop = 4;
            panel.style.paddingBottom = 4;
            panel.style.borderTopLeftRadius = SpeedPillRadius;
            panel.style.borderTopRightRadius = SpeedPillRadius;
            panel.style.borderBottomLeftRadius = SpeedPillRadius;
            panel.style.borderBottomRightRadius = SpeedPillRadius;

            _speedSegments.Clear();

            for (int i = 0; i < StageController.SpeedSteps.Length; i++)
            {
                int index = i;

                var segment = new Button(() => OnSpeedSegmentClicked(index));
                // 기본 테마의 파란 포커스 링이 클릭 뒤에도 남아 혼자 튄다. 마우스 전용 HUD라 포커스를 받지 않는다.
                segment.focusable = false;
                segment.text = $"{StageController.SpeedSteps[i]:0.#}x";
                segment.style.fontSize = 13;
                segment.style.unityFontStyleAndWeight = FontStyle.Bold;
                segment.style.width = SpeedSegmentWidth;
                segment.style.height = SpeedSegmentHeight;
                segment.style.marginLeft = i == 0 ? 0 : 2;
                segment.style.marginRight = 0;
                segment.style.marginTop = 0;
                segment.style.marginBottom = 0;
                segment.style.paddingLeft = 0;
                segment.style.paddingRight = 0;
                segment.style.borderLeftWidth = 0;
                segment.style.borderRightWidth = 0;
                segment.style.borderTopWidth = 0;
                segment.style.borderBottomWidth = 0;

                float radius = SpeedPillRadius - 4f;
                segment.style.borderTopLeftRadius = radius;
                segment.style.borderTopRightRadius = radius;
                segment.style.borderBottomLeftRadius = radius;
                segment.style.borderBottomRightRadius = radius;

                panel.Add(segment);
                _speedSegments.Add(segment);
            }

            RefreshSpeedSegments();

            return panel;
        }

        /// <summary>선택된 단계만 강조한다. 배속은 정적 상태라 씬을 다시 시작해도 유지된다.</summary>
        void RefreshSpeedSegments()
        {
            for (int i = 0; i < _speedSegments.Count; i++)
            {
                bool active = i == StageController.SpeedIndex;

                _speedSegments[i].style.backgroundColor = active ? AccentColor : TrackColor;
                _speedSegments[i].style.color = active ? AccentTextColor : TextColor;
            }
        }

        void OnSpeedSegmentClicked(int index)
        {
            if (_stage == null)
                return;

            _stage.SetSpeed(index);
            RefreshSpeedSegments();
        }

        /// <summary>
        /// HP 디버그 토글. 개발용 스위치라 자원 표시에 섞여 있으면 골드/생명의 무게가 흐려진다.
        /// 일시정지 옆 우상단으로 빼서 "시스템 조작" 쪽으로 묶는다.
        /// </summary>
        VisualElement BuildDebugToggle()
        {
            _hpToggleButton = new Button(OnHpToggleClicked);
            // 기본 테마의 파란 포커스 링이 클릭 뒤에도 남아 혼자 튄다. 마우스 전용 HUD라 포커스를 받지 않는다.
            _hpToggleButton.focusable = false;
            _hpToggleButton.text = string.Empty;

            _hpToggleButton.style.position = Position.Absolute;
            _hpToggleButton.style.top = 8;
            _hpToggleButton.style.right = 8f + PauseButtonSize + 6f;
            _hpToggleButton.style.width = PauseButtonSize;
            _hpToggleButton.style.height = PauseButtonSize;
            _hpToggleButton.style.alignItems = Align.Center;
            _hpToggleButton.style.justifyContent = Justify.Center;

            ClearSpacing(_hpToggleButton);
            SetRadius(_hpToggleButton, PauseButtonSize * 0.5f);

            _hpToggleButton.style.borderLeftWidth = 2;
            _hpToggleButton.style.borderRightWidth = 2;
            _hpToggleButton.style.borderTopWidth = 2;
            _hpToggleButton.style.borderBottomWidth = 2;

            _hpToggleIcon = new GlyphIcon(IconGlyph.Eye);
            _hpToggleIcon.style.width = PauseButtonSize * 0.46f;
            _hpToggleIcon.style.height = PauseButtonSize * 0.46f;
            _hpToggleIcon.style.flexShrink = 0f;
            _hpToggleButton.Add(_hpToggleIcon);

            return _hpToggleButton;
        }

        VisualElement BuildPauseButton()
        {
            _pauseButton = new Button(OnPauseClicked);
            // 기본 테마의 파란 포커스 링이 클릭 뒤에도 남아 혼자 튄다. 마우스 전용 HUD라 포커스를 받지 않는다.
            _pauseButton.focusable = false;
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
            box.style.width = 268;
            box.style.paddingLeft = 20;
            box.style.paddingRight = 20;
            box.style.paddingTop = 20;
            box.style.paddingBottom = 20;
            box.style.alignItems = Align.Stretch;
            SetRadius(box, 14f);

            // 제목: 우상단 일시정지 버튼과 같은 아이콘을 달아 어디서 온 화면인지 바로 잇는다
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.justifyContent = Justify.Center;
            titleRow.style.marginBottom = 6;

            var titleIcon = new GlyphIcon(IconGlyph.Pause);
            titleIcon.Tint = AccentColor;
            titleIcon.style.width = 15;
            titleIcon.style.height = 15;
            titleIcon.style.flexShrink = 0f;
            titleIcon.style.marginRight = 9;
            titleRow.Add(titleIcon);

            var title = new Label("일시정지");
            title.style.color = TextColor;
            title.style.fontSize = 21;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleRow.Add(title);

            box.Add(titleRow);

            var rule = new VisualElement();
            rule.style.height = 1;
            rule.style.marginBottom = 16;
            rule.style.backgroundColor = TrackColor;
            box.Add(rule);

            // 계속하기가 기본 행동이라 혼자 채워진 버튼으로 두고, 나머지는 무게를 낮춘다.
            // 종료는 되돌릴 수 없어 붉은 글자로 따로 구분한다.
            box.Add(MakeMenuButton("계속하기", OnResumeClicked, MenuButtonStyle.Primary));
            box.Add(MakeMenuButton("다시 시작", OnRestartClicked, MenuButtonStyle.Secondary));
            box.Add(MakeMenuButton("종료", OnQuitClicked, MenuButtonStyle.Danger));

            _pauseOverlay.Add(box);

            return _pauseOverlay;
        }

        enum MenuButtonStyle
        {
            Primary = 0,
            Secondary = 1,
            Danger = 2,
        }

        static Button MakeMenuButton(string text, System.Action onClick, MenuButtonStyle style)
        {
            var button = new Button(onClick);
            // 기본 테마의 파란 포커스 링이 클릭 뒤에도 남아 혼자 튄다. 마우스 전용 HUD라 포커스를 받지 않는다.
            button.focusable = false;
            button.text = text;
            button.style.fontSize = 15;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.height = 40;

            // ClearSpacing이 마진을 0으로 밀므로 버튼 간격은 그 뒤에 준다
            ClearSpacing(button);
            SetRadius(button, 9f);

            button.style.marginBottom = 8;
            button.style.borderLeftWidth = style == MenuButtonStyle.Secondary ? 1 : 0;
            button.style.borderRightWidth = style == MenuButtonStyle.Secondary ? 1 : 0;
            button.style.borderTopWidth = style == MenuButtonStyle.Secondary ? 1 : 0;
            button.style.borderBottomWidth = style == MenuButtonStyle.Secondary ? 1 : 0;

            if (style == MenuButtonStyle.Primary)
            {
                button.style.backgroundColor = AccentColor;
                button.style.color = AccentTextColor;

                return button;
            }

            button.style.backgroundColor = TrackColor;
            button.style.color = style == MenuButtonStyle.Danger ? UrgentColor : TextColor;

            if (style == MenuButtonStyle.Secondary)
                SetBorderColor(button, new Color(1f, 1f, 1f, 0.16f));

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
            panel.style.minWidth = 236;

            // 헤더: 좌측 상태 문구 + 우측 남은 초. 숫자를 크게 잡아 멀리서도 읽히게 한다.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;

            _countdownLabel = new Label("다음 웨이브");
            _countdownLabel.style.color = MutedTextColor;
            _countdownLabel.style.fontSize = 12;
            header.Add(_countdownLabel);

            _countdownValueLabel = new Label();
            _countdownValueLabel.style.color = TextColor;
            _countdownValueLabel.style.fontSize = 19;
            _countdownValueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(_countdownValueLabel);

            panel.Add(header);

            // 남은 시간 바. 숫자만으로는 얼마나 급한지 감이 안 와서 같이 보여준다.
            _countdownTrack = new VisualElement();
            _countdownTrack.style.height = 6;
            _countdownTrack.style.marginTop = 6;
            _countdownTrack.style.marginBottom = 10;
            _countdownTrack.style.backgroundColor = TrackColor;
            SetRadius(_countdownTrack, 3f);

            _countdownFill = new VisualElement();
            _countdownFill.style.height = 6;
            _countdownFill.style.backgroundColor = AccentColor;
            SetRadius(_countdownFill, 3f);
            _countdownTrack.Add(_countdownFill);

            panel.Add(_countdownTrack);

            _earlyCallButton = new Button(OnEarlyCallClicked);
            // 기본 테마의 파란 포커스 링이 클릭 뒤에도 남아 혼자 튄다. 마우스 전용 HUD라 포커스를 받지 않는다.
            _earlyCallButton.focusable = false;
            _earlyCallButton.style.fontSize = 14;
            _earlyCallButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _earlyCallButton.style.height = 34;
            _earlyCallButton.style.backgroundColor = AccentColor;
            _earlyCallButton.style.color = AccentTextColor;
            _earlyCallButton.style.borderLeftWidth = 0;
            _earlyCallButton.style.borderRightWidth = 0;
            _earlyCallButton.style.borderTopWidth = 0;
            _earlyCallButton.style.borderBottomWidth = 0;
            ClearSpacing(_earlyCallButton);
            SetRadius(_earlyCallButton, 8f);
            panel.Add(_earlyCallButton);

            return panel;
        }

        static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
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
            // 기본 테마의 파란 포커스 링이 클릭 뒤에도 남아 혼자 튄다. 마우스 전용 HUD라 포커스를 받지 않는다.
            restart.focusable = false;
            restart.text = "다시 시작";
            restart.style.fontSize = 16;
            restart.style.paddingLeft = 24;
            restart.style.paddingRight = 24;
            restart.style.paddingTop = 8;
            restart.style.paddingBottom = 8;
            _resultOverlay.Add(restart);

            return _resultOverlay;
        }

        bool IsLifeLow()
        {
            var data = _stage.Data;

            if (data == null || data.StartLife <= 0)
                return false;

            return _stage.Life <= data.StartLife * LifeWarnRatio;
        }

        void OnEarlyCallClicked()
        {
            if (_stage == null)
                return;

            // 같은 자리의 버튼이지만 1웨이브에서는 "시작", 그 뒤로는 "조기소환"이다
            if (_stage.AwaitingFirstWave)
            {
                _stage.StartFirstWave();
                return;
            }

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
        /// 카메라가 실제로 그리는 영역을 패널 좌표로 환산한다.
        /// 레터박스가 걸려 있으면 camera.rect가 반영된 16:9 안쪽 사각형이 나온다.
        /// </summary>
        static Rect CameraPanelRect(IPanel panel, Camera camera)
        {
            var pixel = camera.pixelRect;

            // 스크린 좌표는 아래가 0, 패널 좌표는 위가 0이라 y를 뒤집어 넘긴다
            Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(pixel.xMin, Screen.height - pixel.yMax));

            Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(pixel.xMax, Screen.height - pixel.yMin));

            return new Rect(topLeft, bottomRight - topLeft);
        }

        /// <summary>길이 size인 요소를 [min, max] 안으로 밀어 넣는다. 칸이 요소보다 좁으면 시작점에 붙인다.</summary>
        static float ClampSpan(float value, float size, float min, float max)
        {
            float lowest = min + FlagMargin;
            float highest = max - size - FlagMargin;

            if (highest < lowest)
                return lowest;

            return Mathf.Clamp(value, lowest, highest);
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
                return;
            }

            var panel = _flagLayer.panel;

            if (panel == null)
                return;

            string title = $"웨이브 {_stage.WaveNumber + 1} 진입 {Mathf.CeilToInt(_stage.NextWaveIn)}초";

            // 조기소환은 보상 카드를 포기하는 거래다. 누르기 전에 알 수 있어야 한다.
            string bonus = $"조기소환 +{_stage.EarlyCallBonus}G · 보상 없음";

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

                // 입구는 맵 가장자리에 있어 중심 정렬만 하면 left가 음수로 나가 깃발이 반쯤 잘린다.
                // 카메라가 실제로 그리는 사각형(레터박스가 걸리면 그 안쪽) 안으로 밀어 넣는다.
                var bounds = CameraPanelRect(panel, camera);

                float left = ClampSpan(position.x - FlagWidth * 0.5f, FlagWidth, bounds.xMin, bounds.xMax);
                float top = ClampSpan(position.y - height - FlagLift, height, bounds.yMin, bounds.yMax);

                flag.Root.style.left = left;
                flag.Root.style.top = top;
                flag.Root.style.display = DisplayStyle.Flex;
                flag.Title.text = title;
                flag.Bonus.text = bonus;

                shown++;
            }

            HideFlagsFrom(shown);

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

            // 켜짐/꺼짐을 문구 대신 채움으로 보여준다. 켜져 있으면 눈이 어둡게 파인 금색 알약이 된다.
            bool on = _debugView.DisplayEnabled;

            _hpToggleButton.style.backgroundColor = on ? AccentColor : PanelColor;
            SetBorderColor(_hpToggleButton, on ? AccentColor : PauseBorderColor);

            _hpToggleIcon.Tint = on ? AccentTextColor : MutedTextColor;
            _hpToggleButton.tooltip = on ? "HP 디버그 끄기" : "HP 디버그 켜기";
        }

        static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
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

            _goldLabel.text = _stage.Gold.ToString();
            _lifeLabel.text = _stage.Life.ToString();
            _waveLabel.text = $"웨이브 {_stage.WaveNumber}/{_stage.TotalWaves}";

            // 생명은 잃기만 하는 값이라 바닥이 보이기 시작할 때 눈에 걸려야 한다
            _lifeLabel.style.color = IsLifeLow() ? UrgentColor : TextColor;

            RefreshSpeedSegments();
            RefreshHpToggle();

            RefreshCountdown();
            RefreshResult();
        }

        void RefreshCountdown()
        {
            if (_countdownLabel == null)
                return;

            // 1웨이브는 카운트다운이 없다. 타워를 다 짓고 플레이어가 직접 시작한다.
            if (_stage.AwaitingFirstWave)
            {
                _countdownLabel.text = "준비";
                _countdownValueLabel.text = string.Empty;
                _countdownTrack.style.display = DisplayStyle.None;

                ShowEarlyCall(true);
                SetEarlyCallEnabled(true);

                _earlyCallButton.text = "웨이브 시작";
                _earlyCallButton.tooltip = "타워를 다 지었으면 눌러 1웨이브를 시작한다. 시작 보너스 골드는 없다.";
                return;
            }

            // "없음" 표기는 정말 웨이브가 다 나왔을 때만. 보상 선택 등으로 잠시 막힌 상태와 구분한다.
            if (_stage.AllWavesStarted)
            {
                _countdownLabel.text = "남은 웨이브 없음";
                _countdownValueLabel.text = string.Empty;
                _countdownTrack.style.display = DisplayStyle.None;
                ShowEarlyCall(false);
                return;
            }

            // 보스를 처치할 때까지 카운트다운이 멈춘다. 숫자가 굳은 이유를 알려준다.
            if (_stage.BossGateBlocking)
            {
                _countdownLabel.text = "보스를 처치해야 넘어간다";
                _countdownValueLabel.text = string.Empty;
                _countdownTrack.style.display = DisplayStyle.None;
                ShowEarlyCall(false);
                return;
            }

            _countdownLabel.text = "다음 웨이브";
            _countdownValueLabel.text = $"{Mathf.CeilToInt(_stage.NextWaveIn)}초";

            _countdownTrack.style.display = DisplayStyle.Flex;

            float ratio = CountdownRatio();

            _countdownFill.style.width = Length.Percent(ratio * 100f);

            // 임박하면 붉게. 숫자를 읽지 않아도 눈에 걸리게 한다.
            bool urgent = ratio <= CountdownUrgentRatio;

            _countdownFill.style.backgroundColor = urgent ? UrgentColor : AccentColor;
            _countdownValueLabel.style.color = urgent ? UrgentColor : TextColor;

            // 스폰 중에는 숨기지 않고 비활성으로 남긴다. 웨이브마다 나타났다 사라지면 UI가 튄다.
            if (_stage.IsWaveSpawning)
            {
                ShowEarlyCall(true);
                SetEarlyCallEnabled(false);
                _earlyCallButton.text = "소환 중";
                return;
            }

            // 입구 깃발과 함께 띄운다. 깃발은 "어디로" 오는지, 이 버튼은 "언제/얼마" 쪽이라 역할이 겹치지 않는다.
            if (!_stage.CanCallEarly)
            {
                ShowEarlyCall(false);
                return;
            }

            ShowEarlyCall(true);
            SetEarlyCallEnabled(true);

            _earlyCallButton.text = $"조기소환 +{_stage.EarlyCallBonus}G";
            _earlyCallButton.tooltip = "남은 대기 시간을 건너뛰고 다음 웨이브를 앞당긴다. 특성 선택은 그대로 진행된다.";
        }

        void SetEarlyCallEnabled(bool enabled)
        {
            _earlyCallButton.SetEnabled(enabled);
            _earlyCallButton.style.backgroundColor = enabled ? AccentColor : TrackColor;
            _earlyCallButton.style.color = enabled ? AccentTextColor : MutedTextColor;
        }

        /// <summary>남은 시간 비율. 1웨이브는 카운트다운 자체가 없어 여기까지 오지 않는다.</summary>
        float CountdownRatio()
        {
            var data = _stage.Data;

            if (data == null)
                return 0f;

            float total = data.WaveInterval;

            if (total <= 0f)
                return 0f;

            return Mathf.Clamp01(_stage.NextWaveIn / total);
        }

        void ShowEarlyCall(bool visible)
        {
            _earlyCallButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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
