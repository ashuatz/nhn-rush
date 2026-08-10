using System.Collections.Generic;
using Rush.Data;
using Rush.Stage;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 획득한 보상을 정리하는 우측 바. 스프레드시트(로그라이트 보상UI).
    /// 접힌 상태에서는 손잡이 탭만 보이고, 클릭으로 펼친다. 펼쳐져 있는 동안 게임이 정지한다.
    /// 보상 선택 직후에는 잠깐 자동으로 펼쳐져 들어가는 연출을 한다 (이때는 정지하지 않음).
    /// 목록의 보상 이름에 마우스를 올리면 설명 팝업이 뜨고, 클릭하면 고정된다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RewardSidebar : MonoBehaviour
    {
        const float PanelWidth = 240f;
        const float TooltipWidth = 260f;
        const int FlashMilliseconds = 1400;

        /// <summary>목록이 비었을 때도 유지할 패널 높이.</summary>
        const float PanelMinHeight = 190f;

        /// <summary>접혀 있을 때 손잡이 탭 높이.</summary>
        const float HandleMinHeight = 90f;

        /// <summary>서랍이 미끄러지는 시간. UI Toolkit 트랜지션은 실시간이라 게임이 멈춰도 돈다.</summary>
        const int DrawerSlideMs = 200;

        static readonly Color PanelColor = new Color(0.1f, 0.1f, 0.14f, 0.92f);

        /// <summary>접힌 손잡이 화살표 색. HUD 강조색과 같은 값이라 UI 전체가 한 벌로 읽힌다.</summary>
        static readonly Color HandleAccentColor = new Color(1f, 0.86f, 0.45f, 1f);
        static readonly Color TooltipColor = new Color(0.07f, 0.07f, 0.1f, 0.97f);
        static readonly Color RowHoverColor = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color RowPinnedColor = new Color(1f, 1f, 1f, 0.16f);

        /// <summary>목록에 묶어 보여줄 계통 순서. 화력이 가장 많아 위로 올린다.</summary>
        static readonly RewardCategory[] CategoryOrder =
        {
            RewardCategory.Firepower,
            RewardCategory.Control,
            RewardCategory.Placement,
            RewardCategory.Economy,
        };

        static void ClearSpacing(VisualElement element)
        {
            element.style.marginLeft = 0;
            element.style.marginRight = 0;
            element.style.marginTop = 0;
            element.style.marginBottom = 0;
        }

        /// <summary>
        /// 기본 테마가 버튼에 얹는 테두리와 포커스 링을 걷어낸다.
        /// 안 걷으면 회색 1px 테두리가 남고, 한 번 누른 뒤에는 파란 포커스 링이 계속 붙어 혼자 튄다.
        /// </summary>
        static void StripDefaultChrome(Button button)
        {
            button.focusable = false;

            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
        }

        static Color CategoryColor(RewardCategory category)
        {
            if (category == RewardCategory.Firepower)
                return new Color(1f, 0.52f, 0.38f, 1f);

            if (category == RewardCategory.Economy)
                return new Color(1f, 0.84f, 0.42f, 1f);

            if (category == RewardCategory.Placement)
                return new Color(0.46f, 0.74f, 1f, 1f);

            return new Color(0.72f, 0.6f, 1f, 1f);
        }

        [SerializeField] StageController _stage;
        [SerializeField] RewardSystem _rewards;

        VisualElement _root;
        VisualElement _panel;
        Label _titleLabel;
        Button _handle;
        ScrollView _list;

        VisualElement _tooltip;
        Label _tooltipTitle;
        Label _tooltipMeta;
        Label _tooltipDesc;
        Label _tooltipNote;
        RewardDefinition _tooltipPinned;

        /// <summary>패널이 펼쳐져 있는지 (고정 + 자동 노출 둘 다 포함).</summary>
        bool _open;

        bool _pinnedOpen;
        IVisualElementScheduledItem _flashClose;

        void OnEnable()
        {
            BuildUI(GetComponent<UIDocument>().rootVisualElement);

            if (_rewards != null)
            {
                _rewards.OfferChanged += RefreshList;
                _rewards.CardAcquired += OnCardAcquired;
            }

            RefreshList();
            ApplyOpenState(animate: false);
        }

        void OnDisable()
        {
            if (_rewards != null)
            {
                _rewards.OfferChanged -= RefreshList;
                _rewards.CardAcquired -= OnCardAcquired;
            }

            CancelFlash();

            if (_pinnedOpen && _stage != null)
                _stage.SetUiPause(false);

            _pinnedOpen = false;

            if (_root != null)
            {
                _root.RemoveFromHierarchy();
                _root = null;
            }
        }

        static Color RarityColor(RewardRarity rarity)
        {
            if (rarity == RewardRarity.Common)
                return new Color(0.65f, 0.65f, 0.65f, 1f);

            if (rarity == RewardRarity.Rare)
                return new Color(0.35f, 0.6f, 1f, 1f);

            if (rarity == RewardRarity.Heroic)
                return new Color(0.75f, 0.4f, 1f, 1f);

            return new Color(1f, 0.65f, 0.2f, 1f);
        }

        static string RarityLabel(RewardRarity rarity)
        {
            if (rarity == RewardRarity.Common)
                return "일반";

            if (rarity == RewardRarity.Rare)
                return "희귀";

            if (rarity == RewardRarity.Heroic)
                return "영웅";

            return "전설";
        }

        static string CategoryLabel(RewardCategory category)
        {
            if (category == RewardCategory.Firepower)
                return "화력";

            if (category == RewardCategory.Economy)
                return "경제";

            if (category == RewardCategory.Placement)
                return "배치";

            return "통제";
        }

        void BuildUI(VisualElement root)
        {
            _root = new VisualElement();
            _root.style.position = Position.Absolute;
            _root.style.right = 0;
            _root.style.top = 90;
            _root.style.bottom = 140;
            _root.style.flexDirection = FlexDirection.Row;

            // 손잡이와 패널을 같은 중심선에 둔다. 한쪽만 위로 붙이면 펼칠 때 손잡이가 튄다.
            _root.style.alignItems = Align.Center;

            // 시작은 닫힌 위치. 트랜지션을 걸기 전에 잡아둬야 첫 프레임에 미끄러지지 않는다.
            _root.style.translate = new Translate(PanelWidth, 0f);
            _root.style.transitionProperty = new List<StylePropertyName> { "translate" };
            _root.style.transitionDuration = new List<TimeValue> { new TimeValue(DrawerSlideMs, TimeUnit.Millisecond) };
            _root.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) };

            // 접기/펼치기 손잡이
            _handle = new Button(TogglePinned);
            _handle.style.width = 26;
            _handle.style.minHeight = HandleMinHeight;
            // 패널과 같은 색을 써서 펼쳤을 때 손잡이-패널 이음매가 보이지 않게 한다
            _handle.style.backgroundColor = PanelColor;
            _handle.style.color = Color.white;
            _handle.style.fontSize = 17;
            _handle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _handle.style.whiteSpace = WhiteSpace.Normal;
            ClearSpacing(_handle);
            StripDefaultChrome(_handle);

            // 화면 오른쪽 끝에 붙으므로 왼쪽만 둥글린다
            _handle.style.borderTopLeftRadius = 10;
            _handle.style.borderBottomLeftRadius = 10;
            _root.Add(_handle);

            _panel = new VisualElement();
            _panel.style.width = PanelWidth;
            _panel.style.backgroundColor = PanelColor;
            _panel.style.paddingLeft = 10;
            _panel.style.paddingRight = 10;
            _panel.style.paddingTop = 10;
            _panel.style.paddingBottom = 10;

            // 왼쪽은 손잡이에 맞닿고 오른쪽은 화면 끝이라 모서리를 둥글리지 않는다.
            // 둥글리면 손잡이와 사이에 홈이 파여 두 조각으로 보인다 (바깥 실루엣은 손잡이가 만든다).
            //
            // 목록이 비어도 높이를 확보한다. 안 그러면 제목 한 줄 높이로 쪼그라들어
            // 손잡이보다 납작해진 상자가 뜬금없이 붙어 있는 모양이 된다.
            _panel.style.minHeight = PanelMinHeight;

            // 보상이 쌓이면 목록이 패널을 밀어 올려 서랍 대역(top 90 ~ bottom 140) 밖으로 자라고,
            // 화면 위아래로 삐져나간 채 잘린다. alignItems Center는 늘리지 않는 대신 넘치는 것도 막지 않는다.
            // 대역 높이로 상한을 걸어 남는 만큼은 아래 ScrollView가 스크롤로 흡수하게 한다.
            _panel.style.maxHeight = Length.Percent(100);

            _titleLabel = new Label("획득 특성");
            _titleLabel.style.color = Color.white;
            _titleLabel.style.fontSize = 14;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.marginBottom = 8;
            _panel.Add(_titleLabel);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.flexGrow = 1;

            // 상한에 걸린 패널 안에서 줄어들 수 있어야 스크롤이 돈다. 안 줄어들면 목록이 그대로 커진다.
            _list.style.flexShrink = 1;
            _panel.Add(_list);

            // 대역이 190보다 낮은 창(아주 납작한 비율)에서는 minHeight가 maxHeight를 이겨 다시 넘친다.
            // 남은 높이보다 큰 최소 높이는 요구하지 않는다.
            _root.RegisterCallback<GeometryChangedEvent>(evt =>
                _panel.style.minHeight = Mathf.Min(PanelMinHeight, evt.newRect.height));

            // 손잡이를 패널 높이에 맞춰 한 덩어리로 보이게 한다.
            // 목록 길이에 따라 패널 높이가 변하므로 레이아웃이 잡힐 때마다 다시 맞춘다.
            // 접혀 있을 때도 맞춰둬야 펼치는 순간 손잡이 크기가 튀지 않는다.
            _panel.RegisterCallback<GeometryChangedEvent>(evt => _handle.style.minHeight = evt.newRect.height);

            _root.Add(_panel);

            BuildTooltip();

            // 화면 앵커 서랍이라 레터박스 안쪽 칸에 넣는다.
            // 칸이 잘라내므로 접힌 상태(오른쪽으로 밀어낸 위치)도 띠 위로 보이지 않는다.
            UiLayers.Content(root).Add(_root);
        }

        /// <summary>설명 팝업. 런타임 UI Toolkit은 기본 tooltip을 그려주지 않아 직접 만든다.</summary>
        void BuildTooltip()
        {
            _tooltip = new VisualElement();
            _tooltip.style.position = Position.Absolute;
            _tooltip.style.right = PanelWidth + 8f;
            _tooltip.style.top = 0;
            _tooltip.style.width = TooltipWidth;
            _tooltip.style.backgroundColor = TooltipColor;
            _tooltip.style.paddingLeft = 10;
            _tooltip.style.paddingRight = 10;
            _tooltip.style.paddingTop = 8;
            _tooltip.style.paddingBottom = 8;
            _tooltip.style.borderTopLeftRadius = 6;
            _tooltip.style.borderTopRightRadius = 6;
            _tooltip.style.borderBottomLeftRadius = 6;
            _tooltip.style.borderBottomRightRadius = 6;
            _tooltip.style.display = DisplayStyle.None;
            _tooltip.pickingMode = PickingMode.Ignore;

            _tooltipTitle = new Label();
            _tooltipTitle.style.fontSize = 14;
            _tooltipTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _tooltipTitle.style.whiteSpace = WhiteSpace.Normal;
            _tooltip.Add(_tooltipTitle);

            _tooltipMeta = new Label();
            _tooltipMeta.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            _tooltipMeta.style.fontSize = 10;
            _tooltipMeta.style.marginBottom = 5;
            _tooltip.Add(_tooltipMeta);

            _tooltipDesc = new Label();
            _tooltipDesc.style.color = new Color(0.88f, 0.88f, 0.88f, 1f);
            _tooltipDesc.style.fontSize = 12;
            _tooltipDesc.style.whiteSpace = WhiteSpace.Normal;
            _tooltip.Add(_tooltipDesc);

            _tooltipNote = new Label();
            _tooltipNote.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            _tooltipNote.style.fontSize = 10;
            _tooltipNote.style.whiteSpace = WhiteSpace.Normal;
            _tooltipNote.style.marginTop = 5;
            _tooltip.Add(_tooltipNote);

            // 내용에 따라 높이가 달라지므로 레이아웃이 잡힌 뒤 화면 밖으로 나가지 않게 보정한다
            _tooltip.RegisterCallback<GeometryChangedEvent>(evt => ClampTooltip());

            _root.Add(_tooltip);
        }

        void ShowTooltip(RewardDefinition card, int stacks, VisualElement row)
        {
            if (_tooltip == null || card == null)
                return;

            _tooltipTitle.text = stacks > 1 ? $"{card.DisplayName} x{stacks}" : card.DisplayName;
            _tooltipTitle.style.color = RarityColor(card.Rarity);

            _tooltipMeta.text = $"{RarityLabel(card.Rarity)} · {CategoryLabel(card.Category)}";

            _tooltipDesc.text = card.Description;

            if (string.IsNullOrEmpty(card.ConditionNote))
            {
                _tooltipNote.style.display = DisplayStyle.None;
            }
            else
            {
                _tooltipNote.style.display = DisplayStyle.Flex;
                _tooltipNote.text = card.ConditionNote;
            }

            _tooltip.style.top = row.worldBound.yMin - _root.worldBound.yMin;
            _tooltip.style.display = DisplayStyle.Flex;

            ClampTooltip();
        }

        /// <summary>다른 행이 고정되어 있던 하이라이트를 지운다.</summary>
        void ClearPinnedHighlight()
        {
            if (_list == null)
                return;

            foreach (var child in _list.Children())
                child.style.backgroundColor = Color.clear;
        }

        void HideTooltip()
        {
            if (_tooltip == null)
                return;

            _tooltip.style.display = DisplayStyle.None;
        }

        void ClampTooltip()
        {
            if (_root == null || _tooltip == null)
                return;

            if (_tooltip.style.display == DisplayStyle.None)
                return;

            float maxTop = _root.layout.height - _tooltip.layout.height;

            if (maxTop < 0f)
                maxTop = 0f;

            if (_tooltip.layout.yMin <= maxTop)
                return;

            _tooltip.style.top = maxTop;
        }

        void TogglePinned()
        {
            _pinnedOpen = !_pinnedOpen;

            // 펼쳐져 있는 동안에는 게임이 진행되지 않는다 (기획: 로그라이트 보상UI)
            if (_stage != null)
                _stage.SetUiPause(_pinnedOpen);

            CancelFlash();
            ApplyOpenState(animate: false);
        }

        void OnCardAcquired(RewardDefinition card)
        {
            RefreshList();

            if (_pinnedOpen)
                return;

            // 보상이 들어가는 연출: 잠깐 펼쳤다가 자동으로 접는다 (게임 정지 없음)
            SetPanelVisible(true);

            CancelFlash();
            _flashClose = _root.schedule.Execute(() =>
            {
                if (!_pinnedOpen)
                    SetPanelVisible(false);
            });
            _flashClose.ExecuteLater(FlashMilliseconds);
        }

        void CancelFlash()
        {
            if (_flashClose == null)
                return;

            _flashClose.Pause();
            _flashClose = null;
        }

        void ApplyOpenState(bool animate)
        {
            SetPanelVisible(_pinnedOpen);
        }

        void SetPanelVisible(bool visible)
        {
            if (_panel == null)
                return;

            _open = visible;

            // display 토글은 애니메이션이 걸리지 않는다. 서랍 전체를 옆으로 밀어 감춘다.
            // 패널만 화면 밖으로 나가고 손잡이는 오른쪽 끝에 그대로 남는다.
            _root.style.translate = new Translate(visible ? 0f : PanelWidth, 0f);

            if (!visible)
            {
                _tooltipPinned = null;
                HideTooltip();
            }

            UpdateHandleText(visible);
        }

        /// <summary>
        /// 손잡이는 방향 표시만 한다.
        /// 개수는 펼쳤을 때 제목("획득 특성 N")이 이미 보여주므로 접힌 탭에서까지 반복할 이유가 없다.
        ///
        /// 다만 배경이 패널과 같은 어두운 색이라, 글자까지 흐리면 탭이 있는지조차 안 보인다.
        /// 접혀 있을 때는 화살표를 강조색으로 올려 "여기 뭔가 있다"를 남긴다.
        /// </summary>
        void UpdateHandleText(bool visible)
        {
            _handle.text = visible ? ">" : "<";
            _handle.style.color = visible ? Color.white : HandleAccentColor;
        }

        void RefreshList()
        {
            if (_list == null || _rewards == null)
                return;

            _list.Clear();

            _tooltipPinned = null;
            HideTooltip();

            // 계통별로 묶어서 보여준다. 평평한 목록이면 지금 화력에 몰렸는지 경제에 몰렸는지가 안 읽힌다.
            int total = 0;

            foreach (var category in CategoryOrder)
            {
                bool headerAdded = false;

                foreach (var pair in _rewards.OwnedStacks)
                {
                    if (pair.Key == null || pair.Value <= 0)
                        continue;

                    if (pair.Key.Category != category)
                        continue;

                    if (!headerAdded)
                    {
                        _list.Add(BuildGroupHeader(category));
                        headerAdded = true;
                    }

                    _list.Add(BuildRow(pair.Key, pair.Value));
                    total += pair.Value;
                }
            }

            _titleLabel.text = total > 0 ? $"획득 특성 {total}" : "획득 특성";

            UpdateHandleText(_open);
        }

        static VisualElement BuildGroupHeader(RewardCategory category)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginTop = 6;
            header.style.marginBottom = 3;

            var label = new Label(CategoryLabel(category));
            label.style.color = CategoryColor(category);
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginRight = 6;
            header.Add(label);

            // 제목 오른쪽을 채우는 얇은 선. 그룹 경계를 눈으로 끊어준다.
            var rule = new VisualElement();
            rule.style.height = 1;
            rule.style.flexGrow = 1;
            rule.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            header.Add(rule);

            return header;
        }

        VisualElement BuildRow(RewardDefinition card, int stacks)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.borderTopLeftRadius = 4;
            row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4;
            row.style.borderBottomRightRadius = 4;

            var dot = new VisualElement();
            dot.style.width = 8;
            dot.style.height = 8;
            dot.style.borderTopLeftRadius = 4;
            dot.style.borderTopRightRadius = 4;
            dot.style.borderBottomLeftRadius = 4;
            dot.style.borderBottomRightRadius = 4;
            dot.style.backgroundColor = RarityColor(card.Rarity);
            dot.style.marginRight = 6;
            row.Add(dot);

            var label = new Label(card.DisplayName);
            label.style.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            label.style.fontSize = 12;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexGrow = 1;
            row.Add(label);

            // 중첩은 이름 뒤에 붙이지 않고 오른쪽 뱃지로 뺀다. 이름이 길어도 개수가 밀리지 않는다.
            if (stacks > 1)
            {
                var badge = new Label($"x{stacks}");
                badge.style.color = RarityColor(card.Rarity);
                badge.style.fontSize = 11;
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                badge.style.marginLeft = 6;
                badge.style.flexShrink = 0f;
                row.Add(badge);
            }

            // 호버로 설명을 띄우고, 클릭하면 마우스를 떼도 유지되도록 고정한다
            row.RegisterCallback<MouseEnterEvent>(evt =>
            {
                if (_tooltipPinned == null)
                    ShowTooltip(card, stacks, row);

                if (_tooltipPinned != card)
                    row.style.backgroundColor = RowHoverColor;
            });

            row.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                if (_tooltipPinned == null)
                    HideTooltip();

                if (_tooltipPinned != card)
                    row.style.backgroundColor = Color.clear;
            });

            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (_tooltipPinned == card)
                {
                    _tooltipPinned = null;
                    row.style.backgroundColor = RowHoverColor;
                    HideTooltip();
                    return;
                }

                ClearPinnedHighlight();

                _tooltipPinned = card;
                row.style.backgroundColor = RowPinnedColor;
                ShowTooltip(card, stacks, row);
            });

            return row;
        }
    }
}
