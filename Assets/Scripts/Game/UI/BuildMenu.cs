using System.Collections.Generic;
using Rush.Combat;
using Rush.Data;
using Rush.Stage;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 슬롯 클릭 -> 건설/강화/판매 메뉴. UI Toolkit 코드 구성.
    /// 슬롯 픽킹(레이캐스트)도 여기서 처리하며, 버튼에 마우스를 올리면 해당 타워의 사거리를 미리 보여준다.
    ///
    /// 슬롯 위치를 중심으로 상하좌우에 뜨는 아이콘 버튼으로 조작한다.
    ///   빈 슬롯   - 위 보병(칼) / 왼쪽 궁수(활) / 오른쪽 마도(스태프) / 아래 포병(폭탄)
    ///   건설된 슬롯 - 위 강화 / 아래 판매
    /// 분기 선택과 스킬 구매만 이름/설명이 필요해 사이드바에 남겼고, 해당 단계에서만 열린다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BuildMenu : MonoBehaviour
    {
        static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.1f, 0.9f);
        static readonly Color TextColor = Color.white;

        // 건설 라디얼 메뉴
        static readonly Color SlotColor = new Color(0.10f, 0.11f, 0.14f, 0.94f);
        static readonly Color SlotBorderColor = new Color(1f, 0.92f, 0.6f, 0.75f);
        static readonly Color DisabledTint = new Color(0.45f, 0.45f, 0.48f, 1f);
        static readonly Color CostColor = new Color(1f, 0.86f, 0.45f, 1f);

        /// <summary>상하좌우 버튼이 놓이는 가상 원의 지름. 원 자체는 그리지 않는다 (사거리 표시와 겹친다).</summary>
        const float RadialDiameter = 180f;

        const float SlotSize = 54f;

        [SerializeField] StageController _stage;
        [SerializeField] TowerData[] _towerCatalog;
        [SerializeField] BuildGhostPreview _ghostPreview;

        /// <summary>건설 순간의 연기 연출. 에디터 셋업에서 채운다.</summary>
        [SerializeField] GameObject _buildFx;

        UIDocument _doc;
        VisualElement _panel;
        Label _titleLabel;
        VisualElement _buttonArea;
        VisualElement _radial;

        /// <summary>빈 슬롯용 계열 4종. 구성이 고정이라 한 번 만들고 계속 쓴다.</summary>
        readonly List<RadialSlot> _buildSlots = new List<RadialSlot>(4);

        /// <summary>건설된 슬롯용 강화/판매. 버튼은 재사용하고 비용 문구만 갈아끼운다.</summary>
        readonly List<RadialSlot> _manageSlots = new List<RadialSlot>(2);

        TowerSlot _selected;
        Camera _camera;

        enum RadialDirection
        {
            Up = 0,
            Left = 1,
            Right = 2,
            Down = 3,
        }

        enum RadialAction
        {
            Build = 0,
            Upgrade = 1,
            Sell = 2,
        }

        /// <summary>라디얼 버튼 하나. 골드 변동 때 통째로 다시 만들지 않고 상태만 갱신하려고 들고 있는다.</summary>
        sealed class RadialSlot
        {
            public Button Button;
            public GlyphIcon Icon;
            public Label Label;
            public RadialAction Action;

            /// <summary>Build 전용 고정 비용. 강화/판매는 매번 다시 계산한다.</summary>
            public int Cost;
        }

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            _camera = Camera.main;

            BuildUI(_doc.rootVisualElement);

            if (_stage != null)
                _stage.Changed += RefreshButtons;
        }

        void OnDisable()
        {
            HideGhost();

            if (_stage != null)
                _stage.Changed -= RefreshButtons;

            if (_panel != null)
            {
                _panel.RemoveFromHierarchy();
                _panel = null;
            }

            if (_radial != null)
            {
                _radial.RemoveFromHierarchy();
                _radial = null;
            }

            _buildSlots.Clear();
            _manageSlots.Clear();
        }

        /// <summary>버튼들은 슬롯의 화면 위치를 따라간다. 카메라가 움직여도 어긋나지 않게 매 프레임 갱신.</summary>
        void LateUpdate()
        {
            // 라디얼이 떠 있는 조건 = 빈 슬롯이 선택된 상태
            if (_radial == null || _selected == null || _selected.IsOccupied)
                return;

            UpdateRadialPosition();
        }

        void UpdateRadialPosition()
        {
            if (_selected == null || _doc == null)
                return;

            if (_camera == null)
                _camera = Camera.main;

            var panel = _doc.rootVisualElement.panel;

            if (panel == null || _camera == null)
                return;

            Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(
                panel, _selected.BuildPosition, _camera);

            _radial.style.left = point.x - RadialDiameter * 0.5f;
            _radial.style.top = point.y - RadialDiameter * 0.5f;
        }

        void Update()
        {
            if (!Input.GetMouseButtonDown(0))
                return;

            if (IsPointerOverUI())
                return;

            PickSlot();
        }

        void BuildUI(VisualElement root)
        {
            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.right = 12;
            _panel.style.top = 60;
            _panel.style.width = 230;
            _panel.style.backgroundColor = PanelColor;
            _panel.style.paddingLeft = 14;
            _panel.style.paddingRight = 14;
            _panel.style.paddingTop = 12;
            _panel.style.paddingBottom = 14;
            _panel.style.borderTopLeftRadius = 6;
            _panel.style.borderTopRightRadius = 6;
            _panel.style.borderBottomLeftRadius = 6;
            _panel.style.borderBottomRightRadius = 6;
            _panel.style.display = DisplayStyle.None;

            _titleLabel = new Label();
            _titleLabel.style.color = TextColor;
            _titleLabel.style.fontSize = 14;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.marginBottom = 8;
            _panel.Add(_titleLabel);

            _buttonArea = new VisualElement();
            _panel.Add(_buttonArea);

            root.Add(_panel);

            BuildRadial(root);
        }

        /// <summary>
        /// 상하좌우 슬롯을 담는 빈 컨테이너. 위치는 매 프레임 슬롯 월드 좌표에서 다시 계산한다.
        /// 테두리 원은 그리지 않는다 - 슬롯의 사거리 표시 원과 겹쳐 읽기 어려워진다.
        /// 컨테이너 자체는 픽킹에서 빼서 버튼 사이 빈 공간을 클릭하면 선택이 풀리도록 둔다.
        /// </summary>
        void BuildRadial(VisualElement root)
        {
            _radial = new VisualElement();
            _radial.style.position = Position.Absolute;
            _radial.style.width = RadialDiameter;
            _radial.style.height = RadialDiameter;
            _radial.style.display = DisplayStyle.None;
            _radial.pickingMode = PickingMode.Ignore;

            root.Add(_radial);
        }

        static void SetBorder(VisualElement element, float width, Color color)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        /// <summary>기본 테마가 넣는 마진/패딩을 걷어낸다. 남아 있으면 원형 버튼 안에서 내용이 밀린다.</summary>
        static void SetSpacing(VisualElement element, float value)
        {
            element.style.marginLeft = value;
            element.style.marginRight = value;
            element.style.marginTop = value;
            element.style.marginBottom = value;
            element.style.paddingLeft = value;
            element.style.paddingRight = value;
            element.style.paddingTop = value;
            element.style.paddingBottom = value;
        }

        static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        bool IsPointerOverUI()
        {
            var panel = _doc.rootVisualElement.panel;

            if (panel == null)
                return false;

            Vector2 screenPos = Input.mousePosition;
            screenPos.y = Screen.height - screenPos.y;

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, screenPos);
            var picked = panel.Pick(panelPos);

            return picked != null;
        }

        void PickSlot()
        {
            if (_camera == null)
            {
                _camera = Camera.main;

                if (_camera == null)
                    return;
            }

            var ray = _camera.ScreenPointToRay(Input.mousePosition);

            if (!Physics.Raycast(ray, out var hit, 300f))
            {
                Select(null);
                return;
            }

            var slot = hit.collider.GetComponentInParent<TowerSlot>();

            Select(slot);
        }

        void Select(TowerSlot slot)
        {
            HideGhost();

            if (_selected != null)
                _selected.SetSelected(false);

            _selected = slot;

            if (_selected != null)
                _selected.SetSelected(true);

            RefreshButtons();
        }

        void RefreshButtons()
        {
            if (_panel == null)
                return;

            // 버튼을 다시 만들면 호버 상태가 끊기므로 남아 있던 고스트도 같이 정리한다
            HideGhost();

            // 승리/패배 후에는 건설 조작을 막고 메뉴를 닫는다
            if (_stage == null || !_stage.IsPlayable)
            {
                if (_selected != null)
                {
                    _selected.SetSelected(false);
                    _selected = null;
                }

                _panel.style.display = DisplayStyle.None;
                HideRadial();
                return;
            }

            if (_selected == null)
            {
                _panel.style.display = DisplayStyle.None;
                HideRadial();
                return;
            }

            _buttonArea.Clear();

            if (_selected.IsOccupied)
            {
                ShowManageRadial();

                // 분기/스킬은 이름과 설명이 필요해 사방 4칸에 안 들어간다. 있을 때만 사이드바를 띄운다.
                _panel.style.display = BuildOccupiedSidebar() ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                _panel.style.display = DisplayStyle.None;
                ShowBuildRadial();
            }

            ShowDefaultRange();
        }

        // ── 라디얼 메뉴 ──────────────────────────────────────────────────────

        /// <summary>빈 슬롯: 타워 계열 4종. 구성이 고정이라 처음 한 번만 만들고 이후엔 상태만 갱신한다.</summary>
        void ShowBuildRadial()
        {
            if (_radial == null)
                return;

            if (_buildSlots.Count == 0)
                PopulateBuildSlots();

            if (_buildSlots.Count == 0)
            {
                HideRadial();
                return;
            }

            foreach (var slot in _manageSlots)
                slot.Button.style.display = DisplayStyle.None;

            foreach (var slot in _buildSlots)
            {
                bool affordable = _stage.Gold >= slot.Cost;

                slot.Button.style.display = DisplayStyle.Flex;
                slot.Button.SetEnabled(affordable);
                slot.Icon.Tint = affordable ? TextColor : DisabledTint;
            }

            ShowRadial();
        }

        /// <summary>건설된 슬롯: 위 강화 / 아래 판매.</summary>
        void ShowManageRadial()
        {
            if (_radial == null)
                return;

            if (_manageSlots.Count == 0)
                PopulateManageSlots();

            foreach (var slot in _buildSlots)
                slot.Button.style.display = DisplayStyle.None;

            var tower = _selected.Occupant;

            foreach (var slot in _manageSlots)
                RefreshManageSlot(slot, tower);

            ShowRadial();
        }

        /// <summary>강화 비용은 레벨마다, 판매 환급액은 투자액마다 바뀐다. 문구까지 매번 다시 쓴다.</summary>
        void RefreshManageSlot(RadialSlot slot, Tower tower)
        {
            if (slot.Action == RadialAction.Upgrade)
            {
                // 최종 레벨이면 버튼을 숨긴다. 비활성으로 남기면 골드만 모으면 되는 줄 착각한다.
                if (!tower.CanUpgrade)
                {
                    slot.Button.style.display = DisplayStyle.None;
                    return;
                }

                int cost = tower.UpgradeCost;
                bool affordable = _stage.Gold >= cost;
                var next = tower.Data.Levels[tower.LevelIndex + 1];

                slot.Button.style.display = DisplayStyle.Flex;
                slot.Button.tooltip = $"강화: {next.DisplayName} ({cost}G)";
                slot.Label.text = cost.ToString();
                slot.Button.SetEnabled(affordable);
                slot.Icon.Tint = affordable ? TextColor : DisabledTint;

                return;
            }

            int refund = tower.SellRefund;

            slot.Button.style.display = DisplayStyle.Flex;
            slot.Button.tooltip = $"판매 (+{refund}G)";
            slot.Label.text = $"+{refund}";
            slot.Button.SetEnabled(true);
            slot.Icon.Tint = TextColor;
        }

        void ShowRadial()
        {
            _radial.style.display = DisplayStyle.Flex;

            UpdateRadialPosition();
        }

        void HideRadial()
        {
            if (_radial == null)
                return;

            _radial.style.display = DisplayStyle.None;
        }

        void PopulateBuildSlots()
        {
            if (_towerCatalog == null)
                return;

            foreach (var data in _towerCatalog)
            {
                if (data == null || data.Levels.Length == 0)
                    continue;

                var stat = data.Levels[0];
                var captured = data;

                var slot = CreateSlot(GlyphOf(data.Type), DirectionOf(data.Type), () => OnBuildClicked(captured));

                slot.Action = RadialAction.Build;
                slot.Cost = stat.Cost;
                slot.Label.text = stat.Cost.ToString();
                slot.Button.tooltip = $"{stat.DisplayName} ({stat.Cost}G)";

                float previewRange = stat.Range * RewardSystem.GetStatMods(data.Type).RangeMul;
                AttachRangePreview(slot.Button, previewRange, captured);

                _buildSlots.Add(slot);
            }
        }

        void PopulateManageSlots()
        {
            var upgrade = CreateSlot(IconGlyph.Upgrade, RadialDirection.Up, OnUpgradeClicked);
            upgrade.Action = RadialAction.Upgrade;
            AttachUpgradePreview(upgrade.Button);
            _manageSlots.Add(upgrade);

            var sell = CreateSlot(IconGlyph.Sell, RadialDirection.Down, OnSellClicked);
            sell.Action = RadialAction.Sell;
            _manageSlots.Add(sell);
        }

        RadialSlot CreateSlot(IconGlyph glyph, RadialDirection direction, System.Action onClick)
        {
            var button = new Button(onClick);
            button.text = string.Empty;

            button.style.position = Position.Absolute;
            button.style.width = SlotSize;
            button.style.height = SlotSize;
            button.style.backgroundColor = SlotColor;
            button.style.flexDirection = FlexDirection.Column;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;

            SetSpacing(button, 0f);
            SetBorder(button, 2f, SlotBorderColor);
            SetRadius(button, SlotSize * 0.5f);
            Place(button, direction);

            // 아이콘과 숫자 모두 고정 크기 + 여백 0으로 둔다.
            // 기본 테마의 Label 마진이 끼면 버튼마다 숫자 높이가 미묘하게 어긋난다.
            var icon = new GlyphIcon(glyph);
            icon.style.width = SlotSize * 0.46f;
            icon.style.height = SlotSize * 0.46f;
            icon.style.flexShrink = 0f;
            button.Add(icon);

            var label = new Label();
            label.style.color = CostColor;
            label.style.fontSize = 11;
            label.style.width = Length.Percent(100);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.flexShrink = 0f;
            label.pickingMode = PickingMode.Ignore;
            SetSpacing(label, 0f);
            button.Add(label);

            _radial.Add(button);

            return new RadialSlot { Button = button, Icon = icon, Label = label };
        }

        /// <summary>
        /// 상하좌우 배치. 건설은 위 보병(칼) / 왼쪽 궁수(활) / 오른쪽 마도(스태프) / 아래 포병(폭탄),
        /// 관리는 위 강화 / 아래 판매로 방향을 맞춰 손에 익게 한다.
        /// </summary>
        static void Place(VisualElement element, RadialDirection direction)
        {
            const float center = (RadialDiameter - SlotSize) * 0.5f;
            const float far = RadialDiameter - SlotSize;

            switch (direction)
            {
                case RadialDirection.Up:
                    element.style.left = center;
                    element.style.top = 0f;
                    break;
                case RadialDirection.Left:
                    element.style.left = 0f;
                    element.style.top = center;
                    break;
                case RadialDirection.Right:
                    element.style.left = far;
                    element.style.top = center;
                    break;
                case RadialDirection.Down:
                    element.style.left = center;
                    element.style.top = far;
                    break;
            }
        }

        static IconGlyph GlyphOf(TowerType type)
        {
            switch (type)
            {
                case TowerType.Infantry:
                    return IconGlyph.Sword;
                case TowerType.Archer:
                    return IconGlyph.Bow;
                case TowerType.Mage:
                    return IconGlyph.Staff;
                default:
                    return IconGlyph.Bomb;
            }
        }

        static RadialDirection DirectionOf(TowerType type)
        {
            switch (type)
            {
                case TowerType.Infantry:
                    return RadialDirection.Up;
                case TowerType.Archer:
                    return RadialDirection.Left;
                case TowerType.Mage:
                    return RadialDirection.Right;
                default:
                    return RadialDirection.Down;
            }
        }

        /// <summary>선택 상태의 기본 사거리 표시: 건설된 타워는 자기 사거리, 빈 슬롯은 표시 없음.</summary>
        void ShowDefaultRange()
        {
            if (_selected == null)
                return;

            if (!_selected.IsOccupied)
            {
                _selected.HideRange();
                return;
            }

            _selected.ShowRange(_selected.Occupant.EffectiveRange);
        }

        /// <summary>
        /// 버튼에 마우스를 올리는 동안 해당 사거리를 미리 보여준다.
        /// buildData를 넘기면 건물 실루엣(고스트)도 함께 띄운다 (빈 슬롯 건설 버튼 전용).
        /// </summary>
        void AttachRangePreview(Button button, float radius, TowerData buildData = null)
        {
            button.RegisterCallback<MouseEnterEvent>(evt =>
            {
                if (_selected == null)
                    return;

                _selected.ShowRange(radius);

                if (buildData != null)
                    ShowGhost(buildData);
            });

            button.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                ShowDefaultRange();
                HideGhost();
            });
        }

        /// <summary>
        /// 강화 버튼의 사거리 프리뷰. 버튼을 재사용하므로 사거리를 미리 담아둘 수 없고,
        /// 호버 시점의 레벨로 다음 단계 사거리를 다시 계산한다.
        /// </summary>
        void AttachUpgradePreview(Button button)
        {
            button.RegisterCallback<MouseEnterEvent>(evt =>
            {
                if (_selected == null || !_selected.IsOccupied)
                    return;

                var tower = _selected.Occupant;

                if (!tower.CanUpgrade)
                    return;

                var next = tower.Data.Levels[tower.LevelIndex + 1];

                _selected.ShowRange(next.Range * RewardSystem.GetStatMods(tower.Data.Type).RangeMul);
            });

            button.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                ShowDefaultRange();
                HideGhost();
            });
        }

        void ShowGhost(TowerData data)
        {
            if (_ghostPreview == null)
                return;

            if (_selected == null)
                return;

            _ghostPreview.Show(data.Type, _selected.BuildPosition);
        }

        void HideGhost()
        {
            if (_ghostPreview == null)
                return;

            _ghostPreview.Hide();
        }

        /// <summary>
        /// 강화/판매는 라디얼로 뺐고, 분기 선택과 스킬 구매만 사이드바에 남긴다.
        /// 둘 다 이름과 설명 텍스트가 붙어야 골라지는 항목이라 사방 4칸에 넣을 수 없다.
        /// 표시할 게 하나라도 있으면 true - 없으면 호출부가 사이드바를 아예 닫는다.
        /// </summary>
        bool BuildOccupiedSidebar()
        {
            var tower = _selected.Occupant;

            // 최종 분기: 3단계에서 두 갈래 중 하나를 고른다 (되돌릴 수 없음)
            bool hasBranch = tower.CanChooseBranch;

            // 분기 확정 후: 분기 전용 스킬 구매 (각 3레벨)
            bool hasSkills = tower.BranchChoice != TowerBranchChoice.None && tower.SkillCount > 0;

            if (!hasBranch && !hasSkills)
                return false;

            var stat = tower.CurrentStat;

            _titleLabel.text = $"{stat.DisplayName} (Lv{tower.LevelIndex + 1})";

            var rangeLabel = new Label($"사거리 {tower.EffectiveRange:0.#}");
            rangeLabel.style.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            rangeLabel.style.fontSize = 11;
            rangeLabel.style.marginBottom = 6;
            _buttonArea.Add(rangeLabel);

            if (hasBranch)
            {
                AddBranchButton(tower, tower.Data.BranchA, TowerBranchChoice.A);
                AddBranchButton(tower, tower.Data.BranchB, TowerBranchChoice.B);
            }

            if (hasSkills)
                AddSkillButtons(tower);

            return true;
        }

        void AddBranchButton(Tower tower, TowerBranchDef branch, TowerBranchChoice choice)
        {
            if (branch == null || !branch.IsValid)
                return;

            int cost = tower.BranchCost(branch);

            var button = new Button(() => OnBranchClicked(choice));
            button.text = $"분기: {branch.Name} ({cost}G)";
            button.style.marginBottom = 4;
            button.SetEnabled(_stage.Gold >= cost);

            float previewRange = branch.Stat.Range * RewardSystem.GetStatMods(tower.Data.Type).RangeMul;
            AttachRangePreview(button, previewRange);

            _buttonArea.Add(button);
        }

        void AddSkillButtons(Tower tower)
        {
            for (int i = 0; i < tower.SkillCount; i++)
            {
                var skill = tower.GetSkill(i);

                if (skill == null)
                    continue;

                int level = tower.GetSkillLevelAt(i);
                int index = i;

                if (level >= BranchSkillDef.MaxLevel)
                {
                    var maxed = new Label($"{skill.DisplayName} Lv{level} (최대)");
                    maxed.style.color = new Color(0.6f, 0.85f, 0.6f, 1f);
                    maxed.style.fontSize = 11;
                    maxed.style.marginBottom = 4;
                    maxed.tooltip = skill.Description;
                    _buttonArea.Add(maxed);
                    continue;
                }

                int cost = tower.SkillUpgradeCost(i);

                var button = new Button(() => OnSkillClicked(index));
                button.text = $"{skill.DisplayName} Lv{level} > {level + 1} ({cost}G)";
                button.style.marginBottom = 4;
                button.tooltip = skill.Description;
                button.SetEnabled(_stage.Gold >= cost);

                _buttonArea.Add(button);
            }
        }

        void OnBranchClicked(TowerBranchChoice choice)
        {
            if (!CanOperate())
                return;

            if (!_selected.IsOccupied)
                return;

            var tower = _selected.Occupant;

            if (!tower.CanChooseBranch)
                return;

            var branch = choice == TowerBranchChoice.A ? tower.Data.BranchA : tower.Data.BranchB;

            if (!_stage.TrySpend(tower.BranchCost(branch)))
                return;

            tower.ChooseBranch(choice);

            RefreshButtons();
        }

        void OnSkillClicked(int index)
        {
            if (!CanOperate())
                return;

            if (!_selected.IsOccupied)
                return;

            var tower = _selected.Occupant;
            int cost = tower.SkillUpgradeCost(index);

            if (cost <= 0)
                return;

            if (!_stage.TrySpend(cost))
                return;

            tower.UpgradeSkill(index);

            RefreshButtons();
        }

        void OnBuildClicked(TowerData data)
        {
            if (!CanOperate())
                return;

            if (_selected.IsOccupied)
                return;

            if (data.TowerPrefab == null)
            {
                GameLog.Warn("Build", $"{data.name}: 타워 프리팹이 비어 있음");
                return;
            }

            var stat = data.Levels[0];

            if (!_stage.TrySpend(stat.Cost))
                return;

            var go = Instantiate(data.TowerPrefab, _selected.BuildPosition, Quaternion.identity, _selected.transform);
            var tower = go.GetComponent<Tower>();

            if (tower == null)
            {
                GameLog.Warn("Build", $"{data.name}: 프리팹에 Tower 컴포넌트가 없음 - 건설 취소");
                _stage.AddGold(stat.Cost);
                Destroy(go);
                return;
            }

            tower.Initialize(data, _stage);
            _selected.Occupant = tower;

            // 건물이 툭 튀어나오는 대신 연기 한 번으로 등장을 가려준다
            Rush.Fx.OneShotFx.Spawn(_buildFx, _selected.BuildPosition);

            GameLog.Info("Build", $"{stat.DisplayName} 건설 (-{stat.Cost}G)");

            RefreshButtons();
        }

        void OnUpgradeClicked()
        {
            if (!CanOperate())
                return;

            if (!_selected.IsOccupied)
                return;

            var tower = _selected.Occupant;

            if (!tower.CanUpgrade)
                return;

            if (!_stage.TrySpend(tower.UpgradeCost))
                return;

            tower.Upgrade();

            RefreshButtons();
        }

        void OnSellClicked()
        {
            if (!CanOperate())
                return;

            if (!_selected.IsOccupied)
                return;

            var tower = _selected.Occupant;
            int refund = tower.SellRefund;

            // Destroy는 프레임 끝에 반영되므로, 그 사이 남은 발사 요청을 먼저 끊는다
            tower.MarkSold();

            _selected.Occupant = null;
            Destroy(tower.gameObject);

            _stage.AddGold(refund);
            GameLog.Info("Build", $"타워 판매 (+{refund}G)");

            RefreshButtons();
        }

        bool CanOperate()
        {
            if (_stage == null)
                return false;

            if (!_stage.IsPlayable)
                return false;

            if (_selected == null)
                return false;

            return true;
        }
    }
}
