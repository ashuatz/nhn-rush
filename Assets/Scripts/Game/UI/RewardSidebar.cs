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

        static readonly Color PanelColor = new Color(0.1f, 0.1f, 0.14f, 0.92f);
        static readonly Color HandleColor = new Color(0.16f, 0.16f, 0.22f, 0.95f);
        static readonly Color TooltipColor = new Color(0.07f, 0.07f, 0.1f, 0.97f);
        static readonly Color RowHoverColor = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color RowPinnedColor = new Color(1f, 1f, 1f, 0.16f);

        [SerializeField] StageController _stage;
        [SerializeField] RewardSystem _rewards;

        VisualElement _root;
        VisualElement _panel;
        Button _handle;
        ScrollView _list;

        VisualElement _tooltip;
        Label _tooltipTitle;
        Label _tooltipMeta;
        Label _tooltipDesc;
        Label _tooltipNote;
        RewardDefinition _tooltipPinned;

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
            _root.style.alignItems = Align.FlexStart;

            // 접기/펼치기 손잡이
            _handle = new Button(TogglePinned);
            _handle.style.width = 26;
            _handle.style.minHeight = 90;
            _handle.style.backgroundColor = HandleColor;
            _handle.style.color = Color.white;
            _handle.style.fontSize = 12;
            _handle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _handle.style.whiteSpace = WhiteSpace.Normal;
            _handle.style.marginRight = 0;
            _root.Add(_handle);

            _panel = new VisualElement();
            _panel.style.width = PanelWidth;
            _panel.style.backgroundColor = PanelColor;
            _panel.style.paddingLeft = 10;
            _panel.style.paddingRight = 10;
            _panel.style.paddingTop = 10;
            _panel.style.paddingBottom = 10;

            var title = new Label("획득 보상");
            title.style.color = Color.white;
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            _panel.Add(title);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.flexGrow = 1;
            _panel.Add(_list);

            _root.Add(_panel);

            BuildTooltip();

            root.Add(_root);
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

            _panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!visible)
            {
                _tooltipPinned = null;
                HideTooltip();
            }

            UpdateHandleText(visible);
        }

        void UpdateHandleText(bool visible)
        {
            int count = 0;

            if (_rewards != null)
            {
                foreach (var pair in _rewards.OwnedStacks)
                    count += pair.Value;
            }

            _handle.text = visible ? ">" : $"<\n보상\n{count}";
        }

        void RefreshList()
        {
            if (_list == null || _rewards == null)
                return;

            _list.Clear();

            _tooltipPinned = null;
            HideTooltip();

            foreach (var pair in _rewards.OwnedStacks)
            {
                if (pair.Key == null || pair.Value <= 0)
                    continue;

                _list.Add(BuildRow(pair.Key, pair.Value));
            }

            UpdateHandleText(_panel != null && _panel.style.display == DisplayStyle.Flex);
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

            string text = card.DisplayName;

            if (stacks > 1)
                text += $" x{stacks}";

            var label = new Label(text);
            label.style.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            label.style.fontSize = 12;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexGrow = 1;
            row.Add(label);

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
