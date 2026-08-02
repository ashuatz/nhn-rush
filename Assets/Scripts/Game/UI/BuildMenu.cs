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
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BuildMenu : MonoBehaviour
    {
        static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.1f, 0.9f);
        static readonly Color TextColor = Color.white;

        [SerializeField] StageController _stage;
        [SerializeField] TowerData[] _towerCatalog;

        UIDocument _doc;
        VisualElement _panel;
        Label _titleLabel;
        VisualElement _buttonArea;
        TowerSlot _selected;
        Camera _camera;

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
            if (_stage != null)
                _stage.Changed -= RefreshButtons;

            if (_panel != null)
            {
                _panel.RemoveFromHierarchy();
                _panel = null;
            }
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

            // 승리/패배 후에는 건설 조작을 막고 메뉴를 닫는다
            if (_stage == null || !_stage.IsPlayable)
            {
                if (_selected != null)
                {
                    _selected.SetSelected(false);
                    _selected = null;
                }

                _panel.style.display = DisplayStyle.None;
                return;
            }

            if (_selected == null)
            {
                _panel.style.display = DisplayStyle.None;
                return;
            }

            _panel.style.display = DisplayStyle.Flex;
            _buttonArea.Clear();

            if (_selected.IsOccupied)
            {
                BuildOccupiedMenu();
            }
            else
            {
                BuildConstructMenu();
            }

            ShowDefaultRange();
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

            _selected.ShowRange(_selected.Occupant.CurrentStat.Range);
        }

        /// <summary>버튼에 마우스를 올리는 동안 해당 사거리를 미리 보여준다.</summary>
        void AttachRangePreview(Button button, float radius)
        {
            button.RegisterCallback<MouseEnterEvent>(evt =>
            {
                if (_selected == null)
                    return;

                _selected.ShowRange(radius);
            });

            button.RegisterCallback<MouseLeaveEvent>(evt => ShowDefaultRange());
        }

        void BuildConstructMenu()
        {
            _titleLabel.text = "타워 건설";

            if (_towerCatalog == null)
                return;

            foreach (var data in _towerCatalog)
            {
                if (data == null || data.Levels.Length == 0)
                    continue;

                var stat = data.Levels[0];
                var captured = data;

                var button = new Button(() => OnBuildClicked(captured));
                button.text = $"{stat.DisplayName} ({stat.Cost}G)";
                button.style.marginBottom = 4;
                button.SetEnabled(_stage.Gold >= stat.Cost);

                AttachRangePreview(button, stat.Range);

                _buttonArea.Add(button);
            }
        }

        void BuildOccupiedMenu()
        {
            var tower = _selected.Occupant;
            var stat = tower.CurrentStat;

            _titleLabel.text = $"{stat.DisplayName} (Lv{tower.LevelIndex + 1})";

            var rangeLabel = new Label($"사거리 {stat.Range:0.#}");
            rangeLabel.style.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            rangeLabel.style.fontSize = 11;
            rangeLabel.style.marginBottom = 6;
            _buttonArea.Add(rangeLabel);

            if (tower.CanUpgrade)
            {
                var next = tower.Data.Levels[tower.LevelIndex + 1];

                var upgrade = new Button(OnUpgradeClicked);
                upgrade.text = $"강화: {next.DisplayName} ({tower.UpgradeCost}G)";
                upgrade.style.marginBottom = 4;
                upgrade.SetEnabled(_stage.Gold >= tower.UpgradeCost);

                AttachRangePreview(upgrade, next.Range);

                _buttonArea.Add(upgrade);
            }

            var sell = new Button(OnSellClicked);
            sell.text = $"판매 (+{tower.SellRefund}G)";
            _buttonArea.Add(sell);
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
