using Rush.Data;
using Rush.Stage;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 보상 선택 오버레이. 화면을 디밍하고 카드 3장 + 다시뽑기/건너뛰기 버튼을 띄운다.
    /// RewardSystem.OfferChanged 이벤트를 구독만 하고, 조작은 RewardSystem 공개 API를 호출한다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RewardOverlay : MonoBehaviour
    {
        static readonly Color DimColor = new Color(0f, 0f, 0f, 0.7f);
        static readonly Color CardColor = new Color(0.12f, 0.12f, 0.16f, 1f);
        static readonly Color CardHoverColor = new Color(0.18f, 0.18f, 0.24f, 1f);

        [SerializeField] StageController _stage;
        [SerializeField] RewardSystem _rewards;

        VisualElement _overlay;
        VisualElement _cardRow;
        Button _rerollButton;
        Button _skipButton;

        void OnEnable()
        {
            BuildUI(GetComponent<UIDocument>().rootVisualElement);

            if (_rewards != null)
                _rewards.OfferChanged += Refresh;

            Refresh();
        }

        void OnDisable()
        {
            if (_rewards != null)
                _rewards.OfferChanged -= Refresh;

            if (_overlay != null)
            {
                _overlay.RemoveFromHierarchy();
                _overlay = null;
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
            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.right = 0;
            _overlay.style.top = 0;
            _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = DimColor;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.display = DisplayStyle.None;

            var title = new Label("보상 선택");
            title.style.color = Color.white;
            title.style.fontSize = 26;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 18;
            _overlay.Add(title);

            _cardRow = new VisualElement();
            _cardRow.style.flexDirection = FlexDirection.Row;
            _cardRow.style.justifyContent = Justify.Center;
            _overlay.Add(_cardRow);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.Center;
            buttonRow.style.marginTop = 20;
            _overlay.Add(buttonRow);

            _rerollButton = new Button(OnRerollClicked);
            _rerollButton.style.fontSize = 14;
            _rerollButton.style.paddingLeft = 18;
            _rerollButton.style.paddingRight = 18;
            _rerollButton.style.paddingTop = 8;
            _rerollButton.style.paddingBottom = 8;
            _rerollButton.style.marginRight = 12;
            buttonRow.Add(_rerollButton);

            _skipButton = new Button(OnSkipClicked);
            _skipButton.style.fontSize = 14;
            _skipButton.style.paddingLeft = 18;
            _skipButton.style.paddingRight = 18;
            _skipButton.style.paddingTop = 8;
            _skipButton.style.paddingBottom = 8;
            buttonRow.Add(_skipButton);

            root.Add(_overlay);
        }

        void OnRerollClicked()
        {
            if (_rewards != null)
                _rewards.Reroll();
        }

        void OnSkipClicked()
        {
            if (_rewards != null)
                _rewards.Skip();
        }

        void Refresh()
        {
            if (_overlay == null || _rewards == null)
                return;

            if (!_rewards.OfferActive)
            {
                _overlay.style.display = DisplayStyle.None;
                return;
            }

            _overlay.style.display = DisplayStyle.Flex;
            _cardRow.Clear();

            for (int i = 0; i < _rewards.CurrentOffer.Count; i++)
                _cardRow.Add(BuildCard(_rewards.CurrentOffer[i], i));

            var config = _rewards.Config;

            string rerollText = $"다시뽑기 ({_rewards.RerollsLeft}회)";

            if (config.RerollCost > 0)
                rerollText += $" -{config.RerollCost}G";

            _rerollButton.text = rerollText;
            _rerollButton.SetEnabled(_rewards.CanReroll);

            _skipButton.text = $"건너뛰기 (+{config.SkipGold}G)";
        }

        VisualElement BuildCard(RewardDefinition card, int index)
        {
            var box = new VisualElement();
            box.style.width = 210;
            box.style.minHeight = 210;
            box.style.backgroundColor = CardColor;
            box.style.marginLeft = 8;
            box.style.marginRight = 8;
            box.style.paddingLeft = 14;
            box.style.paddingRight = 14;
            box.style.paddingTop = 12;
            box.style.paddingBottom = 12;
            box.style.borderTopLeftRadius = 8;
            box.style.borderTopRightRadius = 8;
            box.style.borderBottomLeftRadius = 8;
            box.style.borderBottomRightRadius = 8;
            box.style.borderTopWidth = 3;
            box.style.borderTopColor = RarityColor(card.Rarity);

            var head = new Label($"{RarityLabel(card.Rarity)} · {CategoryLabel(card.Category)}");
            head.style.color = RarityColor(card.Rarity);
            head.style.fontSize = 11;
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.marginBottom = 6;
            box.Add(head);

            var name = new Label(card.DisplayName);
            name.style.color = Color.white;
            name.style.fontSize = 16;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.marginBottom = 8;
            box.Add(name);

            var desc = new Label(card.Description);
            desc.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            desc.style.fontSize = 12;
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.flexGrow = 1;
            box.Add(desc);

            if (!string.IsNullOrEmpty(card.ConditionNote))
            {
                var note = new Label(card.ConditionNote);
                note.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                note.style.fontSize = 10;
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.marginTop = 6;
                box.Add(note);
            }

            int owned = _rewards.StackOf(card);

            var stack = new Label($"보유 {owned}/{card.StackLimit}");
            stack.style.color = new Color(0.55f, 0.55f, 0.55f, 1f);
            stack.style.fontSize = 10;
            stack.style.marginTop = 6;
            box.Add(stack);

            int captured = index;

            box.RegisterCallback<ClickEvent>(evt => _rewards.Pick(captured));
            box.RegisterCallback<MouseEnterEvent>(evt => box.style.backgroundColor = CardHoverColor);
            box.RegisterCallback<MouseLeaveEvent>(evt => box.style.backgroundColor = CardColor);

            return box;
        }
    }
}
