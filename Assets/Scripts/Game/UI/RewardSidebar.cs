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
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RewardSidebar : MonoBehaviour
    {
        const float PanelWidth = 240f;
        const int FlashMilliseconds = 1400;

        static readonly Color PanelColor = new Color(0.1f, 0.1f, 0.14f, 0.92f);
        static readonly Color HandleColor = new Color(0.16f, 0.16f, 0.22f, 0.95f);

        [SerializeField] StageController _stage;
        [SerializeField] RewardSystem _rewards;

        VisualElement _root;
        VisualElement _panel;
        Button _handle;
        ScrollView _list;

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
            root.Add(_root);
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

            // 카드 설명은 툴팁 대신 행 아래 작은 글씨로 (호버 UI 없이 즉시 확인)
            row.tooltip = card.Description;

            return row;
        }
    }
}
