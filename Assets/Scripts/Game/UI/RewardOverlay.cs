using System.Collections.Generic;
using Rush.Data;
using Rush.Stage;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 보상 선택 오버레이. 화면을 디밍하고 카드 3장 + 다시뽑기 버튼을 띄운다 (1장 선택 강제, 스킵 없음).
    /// RewardSystem.OfferChanged 이벤트를 구독만 하고, 조작은 RewardSystem 공개 API를 호출한다.
    /// 제시될 때는 덱에서 카드가 쭈루룩 흩뿌려진 뒤 3장이 자리에 앉아 뒤집히는 연출을 재생한다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RewardOverlay : MonoBehaviour
    {
        // 노출 연출 타이밍 (UI Toolkit 스케줄러는 실시간 기준이라 게임이 멈춰 있어도 돈다)
        const float DeckRise = 200f;
        const int StreamCount = 8;
        const int StreamIntervalMs = 45;
        const int StreamFlyMs = 340;
        const int DealIntervalMs = 120;
        const int DealFlyMs = 240;
        const int FlipHalfMs = 110;

        static readonly Color DimColor = new Color(0f, 0f, 0f, 0.7f);
        static readonly Color CardColor = new Color(0.12f, 0.12f, 0.16f, 1f);
        static readonly Color CardHoverColor = new Color(0.18f, 0.18f, 0.24f, 1f);
        static readonly Color CardBackColor = new Color(0.15f, 0.16f, 0.26f, 1f);
        static readonly Color CardBackEdgeColor = new Color(0.42f, 0.44f, 0.62f, 1f);

        [SerializeField] StageController _stage;
        [SerializeField] RewardSystem _rewards;

        readonly List<CardVisual> _cardVisuals = new List<CardVisual>(3);

        VisualElement _overlay;
        VisualElement _cardRow;
        VisualElement _streamLayer;
        Button _rerollButton;

        bool _revealing;

        /// <summary>연출 세대. 오퍼가 다시 빌드되면 올려서 이전 연출의 예약 콜백을 무효화한다.</summary>
        int _revealToken;

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

            _revealToken++;
            _revealing = false;
            _cardVisuals.Clear();

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

            var title = new Label("특성 선택");
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
            // 기본 테마의 파란 포커스 링이 클릭 뒤에도 남아 혼자 튄다. 마우스 전용 HUD라 포커스를 받지 않는다.
            _rerollButton.focusable = false;
            _rerollButton.style.fontSize = 14;
            _rerollButton.style.paddingLeft = 18;
            _rerollButton.style.paddingRight = 18;
            _rerollButton.style.paddingTop = 8;
            _rerollButton.style.paddingBottom = 8;
            buttonRow.Add(_rerollButton);

            // 셔플 연출용 카드 뒷면이 날아다니는 레이어 (입력은 통과시킨다)
            _streamLayer = new VisualElement();
            _streamLayer.style.position = Position.Absolute;
            _streamLayer.style.left = 0;
            _streamLayer.style.right = 0;
            _streamLayer.style.top = 0;
            _streamLayer.style.bottom = 0;
            _streamLayer.pickingMode = PickingMode.Ignore;
            _overlay.Add(_streamLayer);

            root.Add(_overlay);
        }

        void OnRerollClicked()
        {
            if (_rewards != null)
                _rewards.Reroll();
        }

        void Refresh()
        {
            if (_overlay == null || _rewards == null)
                return;

            // 진행 중이던 노출 연출의 예약 콜백을 무효화한다 (다시뽑기/취소로 화면이 재구성될 수 있다)
            _revealToken++;
            _revealing = false;

            if (!_rewards.OfferActive)
            {
                _overlay.style.display = DisplayStyle.None;
                _cardRow.Clear();
                _streamLayer.Clear();
                _cardVisuals.Clear();
                return;
            }

            _overlay.style.display = DisplayStyle.Flex;
            _cardRow.Clear();
            _streamLayer.Clear();
            _cardVisuals.Clear();

            for (int i = 0; i < _rewards.CurrentOffer.Count; i++)
                _cardRow.Add(BuildCard(_rewards.CurrentOffer[i], i));

            BeginReveal();

            var config = _rewards.Config;

            // 보스 처치 보상은 다시뽑기가 없는 제시라 버튼을 아예 감춘다 (남은 횟수는 그대로 보존된다)
            if (!_rewards.RerollAllowed)
            {
                _rerollButton.style.display = DisplayStyle.None;
                return;
            }

            _rerollButton.style.display = DisplayStyle.Flex;

            string rerollText = $"다시뽑기 (잔여 {_rewards.RerollsLeft}회)";

            if (config.RerollCost > 0)
                rerollText += $" -{config.RerollCost}G";

            _rerollButton.text = rerollText;

            // 연출이 끝나기 전에는 다시뽑기를 막아 카드가 겹쳐 날아오지 않게 한다
            _rerollButton.SetEnabled(_rewards.CanReroll && !_revealing);
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

            // 등급 색은 뒤집힌 뒤에 드러낸다 (뒷면 상태에서 등급이 새어 나가지 않게)
            box.style.borderTopColor = CardBackEdgeColor;
            box.style.opacity = 0f;

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

            // 뒷면. 뒤집기 연출이 끝나면 감춘다
            var back = new VisualElement();
            back.style.position = Position.Absolute;
            back.style.left = 0;
            back.style.right = 0;
            back.style.top = 0;
            back.style.bottom = 0;
            back.style.backgroundColor = CardBackColor;
            back.style.borderTopLeftRadius = 8;
            back.style.borderTopRightRadius = 8;
            back.style.borderBottomLeftRadius = 8;
            back.style.borderBottomRightRadius = 8;
            back.style.alignItems = Align.Center;
            back.style.justifyContent = Justify.Center;
            back.pickingMode = PickingMode.Ignore;

            var backMark = new Label("?");
            backMark.style.color = CardBackEdgeColor;
            backMark.style.fontSize = 42;
            backMark.style.unityFontStyleAndWeight = FontStyle.Bold;
            back.Add(backMark);

            box.Add(back);

            int captured = index;

            box.RegisterCallback<ClickEvent>(evt =>
            {
                if (_revealing)
                    return;

                _rewards.Pick(captured);
            });

            box.RegisterCallback<MouseEnterEvent>(evt => box.style.backgroundColor = CardHoverColor);
            box.RegisterCallback<MouseLeaveEvent>(evt => box.style.backgroundColor = CardColor);

            _cardVisuals.Add(new CardVisual { Box = box, Back = back, Rarity = RarityColor(card.Rarity) });

            return box;
        }

        /// <summary>연출 중에 다뤄야 하는 카드 1장의 요소 묶음.</summary>
        class CardVisual
        {
            public VisualElement Box;
            public VisualElement Back;
            public Color Rarity;
        }

        /// <summary>카드 레이아웃이 잡히면 노출 연출을 시작한다 (최종 위치를 알아야 덱에서 날아올 수 있다).</summary>
        void BeginReveal()
        {
            _revealing = true;

            if (_cardVisuals.Count == 0)
            {
                _revealing = false;
                return;
            }

            var first = _cardVisuals[0].Box;

            int token = _revealToken;

            EventCallback<GeometryChangedEvent> onLaidOut = null;

            onLaidOut = evt =>
            {
                first.UnregisterCallback(onLaidOut);

                if (token != _revealToken)
                    return;

                PlayReveal(token);
            };

            first.RegisterCallback(onLaidOut);
        }

        void PlayReveal(int token)
        {
            PlayShuffleStream(token);

            int dealStart = StreamCount * StreamIntervalMs;

            for (int i = 0; i < _cardVisuals.Count; i++)
            {
                var visual = _cardVisuals[i];
                var box = visual.Box;

                // box.layout 은 카드 줄 기준 좌표라 덱(줄 가운데)도 같은 기준으로 잡는다
                float fromX = _cardRow.layout.width * 0.5f - box.layout.center.x;
                float fromY = -DeckRise;
                float fromAngle = -10f + i * 8f;

                box.style.translate = ToTranslate(fromX, fromY);
                box.style.rotate = ToRotate(fromAngle);

                bool last = i == _cardVisuals.Count - 1;

                box.schedule.Execute(() =>
                {
                    if (token != _revealToken)
                        return;

                    var fly = box.experimental.animation.Start(0f, 1f, DealFlyMs, (element, t) =>
                    {
                        float k = EaseOut(t);

                        element.style.opacity = Mathf.Min(1f, t * 4f);
                        element.style.translate = ToTranslate(Mathf.Lerp(fromX, 0f, k), Mathf.Lerp(fromY, 0f, k));
                        element.style.rotate = ToRotate(Mathf.Lerp(fromAngle, 0f, k));
                    });

                    fly.OnCompleted(() => FlipCard(visual, last, token));
                })
                .ExecuteLater(dealStart + i * DealIntervalMs);
            }
        }

        /// <summary>덱에서 카드가 쭈루룩 흩뿌려지는 셔플 연출. 뒷면만 날아가고 사라진다.</summary>
        void PlayShuffleStream(int token)
        {
            float deckX = _cardRow.layout.center.x;
            float deckY = _cardRow.layout.yMin - DeckRise;
            float spread = _cardRow.layout.width * 0.5f;

            for (int i = 0; i < StreamCount; i++)
            {
                var mini = new VisualElement();
                mini.style.position = Position.Absolute;
                mini.style.left = deckX - 37f;
                mini.style.top = deckY;
                mini.style.width = 74;
                mini.style.height = 104;
                mini.style.backgroundColor = CardBackColor;
                mini.style.borderTopLeftRadius = 6;
                mini.style.borderTopRightRadius = 6;
                mini.style.borderBottomLeftRadius = 6;
                mini.style.borderBottomRightRadius = 6;
                mini.style.borderTopWidth = 2;
                mini.style.borderTopColor = CardBackEdgeColor;
                mini.style.opacity = 0f;
                mini.pickingMode = PickingMode.Ignore;
                _streamLayer.Add(mini);

                float ratio = StreamCount == 1 ? 0.5f : i / (float)(StreamCount - 1);
                float toX = Mathf.Lerp(-spread, spread, ratio);
                float toY = 90f + (i % 2) * 40f;
                float toAngle = -30f + i * 8f;

                mini.schedule.Execute(() =>
                {
                    if (token != _revealToken)
                    {
                        mini.RemoveFromHierarchy();
                        return;
                    }

                    var fly = mini.experimental.animation.Start(0f, 1f, StreamFlyMs, (element, t) =>
                    {
                        float k = EaseOut(t);

                        element.style.opacity = t < 0.25f ? t * 4f : Mathf.Clamp01((1f - t) * 2.5f);
                        element.style.translate = ToTranslate(toX * k, toY * k);
                        element.style.rotate = ToRotate(toAngle * k);
                    });

                    fly.OnCompleted(mini.RemoveFromHierarchy);
                })
                .ExecuteLater(i * StreamIntervalMs);
            }
        }

        /// <summary>좌우로 눌렀다 펴면서 앞면으로 뒤집는다.</summary>
        void FlipCard(CardVisual visual, bool last, int token)
        {
            if (token != _revealToken)
                return;

            var toEdge = visual.Box.experimental.animation.Start(1f, 0f, FlipHalfMs, (element, v) =>
                element.style.scale = new Scale(new Vector2(v, 1f)));

            toEdge.OnCompleted(() =>
            {
                if (token != _revealToken)
                    return;

                visual.Back.style.display = DisplayStyle.None;
                visual.Box.style.borderTopColor = visual.Rarity;

                var toFace = visual.Box.experimental.animation.Start(0f, 1f, FlipHalfMs, (element, v) =>
                    element.style.scale = new Scale(new Vector2(v, 1f)));

                if (!last)
                    return;

                toFace.OnCompleted(() => EndReveal(token));
            });
        }

        void EndReveal(int token)
        {
            if (token != _revealToken)
                return;

            _revealing = false;

            if (_rewards == null || !_rewards.OfferActive || !_rewards.RerollAllowed)
                return;

            _rerollButton.SetEnabled(_rewards.CanReroll);
        }

        static float EaseOut(float t)
        {
            float inv = 1f - t;

            return 1f - inv * inv * inv;
        }

        static Translate ToTranslate(float x, float y)
        {
            return new Translate(new Length(x, LengthUnit.Pixel), new Length(y, LengthUnit.Pixel));
        }

        static Rotate ToRotate(float degrees)
        {
            return new Rotate(new Angle(degrees, AngleUnit.Degree));
        }
    }
}
