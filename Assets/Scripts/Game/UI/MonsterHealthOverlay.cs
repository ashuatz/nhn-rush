using System.Collections.Generic;
using Rush.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 몬스터 HP 표시 오버레이 (UI Toolkit).
    /// 혼자 있는 몬스터는 머리 위에 바를 직접 붙이고,
    /// 화면상 겹치는 몬스터들은 옆으로 뺀 리스트로 묶은 뒤 설명선(리더 라인)으로 각 개체와 잇는다.
    /// 매 프레임 화면 투영으로 갱신하며, 요소는 풀로 재사용한다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MonsterHealthOverlay : MonoBehaviour
    {
        /// <summary>이 거리(패널 px) 안에 모이면 한 클러스터로 묶는다.</summary>
        const float ClusterRadius = 42f;

        const float BarWidth = 40f;
        const float BarHeight = 5f;
        const float RowHeight = 15f;
        const float HeadOffset = 0.75f;
        const float ListOffsetX = 58f;

        static readonly Color BackColor = new Color(0f, 0f, 0f, 0.65f);
        static readonly Color LineColor = new Color(1f, 1f, 1f, 0.4f);

        struct Entry
        {
            public Monster Monster;
            public Vector2 PanelPos;
            public float HpFraction;
        }

        struct LeaderLine
        {
            public Vector2 From;
            public Vector2 To;
        }

        class BarRow
        {
            public VisualElement Root;
            public Label Name;
            public VisualElement Fill;
        }

        readonly List<Entry> _entries = new List<Entry>(64);
        readonly List<List<int>> _clusters = new List<List<int>>(16);
        readonly List<Vector2> _clusterCentroids = new List<Vector2>(16);
        readonly List<LeaderLine> _lines = new List<LeaderLine>(32);
        readonly List<BarRow> _pool = new List<BarRow>(64);

        UIDocument _doc;
        VisualElement _layer;
        VisualElement _lineLayer;
        Camera _camera;
        int _usedRows;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            _camera = Camera.main;

            BuildUI(_doc.rootVisualElement);
        }

        void OnDisable()
        {
            if (_layer != null)
            {
                _layer.RemoveFromHierarchy();
                _layer = null;
            }

            _pool.Clear();
        }

        void BuildUI(VisualElement root)
        {
            _layer = new VisualElement();
            _layer.pickingMode = PickingMode.Ignore;
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0;
            _layer.style.right = 0;
            _layer.style.top = 0;
            _layer.style.bottom = 0;

            // 설명선은 painter2D로 그린다. 바보다 아래(먼저 추가)에 둔다.
            _lineLayer = new VisualElement();
            _lineLayer.pickingMode = PickingMode.Ignore;
            _lineLayer.style.position = Position.Absolute;
            _lineLayer.style.left = 0;
            _lineLayer.style.right = 0;
            _lineLayer.style.top = 0;
            _lineLayer.style.bottom = 0;
            _lineLayer.generateVisualContent += DrawLines;
            _layer.Add(_lineLayer);

            root.Add(_layer);
        }

        void DrawLines(MeshGenerationContext ctx)
        {
            var painter = ctx.painter2D;
            painter.strokeColor = LineColor;
            painter.lineWidth = 1.5f;

            foreach (var line in _lines)
            {
                painter.BeginPath();
                painter.MoveTo(line.From);
                painter.LineTo(line.To);
                painter.Stroke();
            }
        }

        void Update()
        {
            if (_layer == null)
                return;

            if (_camera == null)
            {
                _camera = Camera.main;

                if (_camera == null)
                    return;
            }

            var panel = _doc.rootVisualElement.panel;

            if (panel == null)
                return;

            CollectEntries(panel);
            BuildClusters();
            Render();

            _lineLayer.MarkDirtyRepaint();
        }

        void CollectEntries(IPanel panel)
        {
            _entries.Clear();

            foreach (var monster in MonsterRegistry.Active)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                Vector3 world = monster.transform.position + Vector3.up * HeadOffset;
                Vector3 screen = _camera.WorldToScreenPoint(world);

                if (screen.z <= 0f)
                    continue;

                Vector2 flipped = new Vector2(screen.x, Screen.height - screen.y);
                Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, flipped);

                _entries.Add(new Entry
                {
                    Monster = monster,
                    PanelPos = panelPos,
                    HpFraction = Mathf.Clamp01(monster.Hp / Mathf.Max(1f, monster.MaxHp)),
                });
            }
        }

        /// <summary>탐욕 클러스터링: 기존 클러스터 중심과 가까우면 합류, 아니면 새 클러스터.</summary>
        void BuildClusters()
        {
            _clusters.Clear();
            _clusterCentroids.Clear();

            for (int i = 0; i < _entries.Count; i++)
            {
                int joined = -1;

                for (int c = 0; c < _clusterCentroids.Count; c++)
                {
                    if ((_entries[i].PanelPos - _clusterCentroids[c]).sqrMagnitude > ClusterRadius * ClusterRadius)
                        continue;

                    joined = c;
                    break;
                }

                if (joined < 0)
                {
                    var members = new List<int> { i };
                    _clusters.Add(members);
                    _clusterCentroids.Add(_entries[i].PanelPos);
                    continue;
                }

                var cluster = _clusters[joined];
                cluster.Add(i);

                // 중심을 이동 평균으로 갱신
                Vector2 sum = Vector2.zero;

                foreach (int index in cluster)
                    sum += _entries[index].PanelPos;

                _clusterCentroids[joined] = sum / cluster.Count;
            }
        }

        void Render()
        {
            _usedRows = 0;
            _lines.Clear();

            float panelWidth = _doc.rootVisualElement.resolvedStyle.width;
            float panelHeight = _doc.rootVisualElement.resolvedStyle.height;

            for (int c = 0; c < _clusters.Count; c++)
            {
                var cluster = _clusters[c];

                if (cluster.Count == 1)
                {
                    RenderSingle(_entries[cluster[0]]);
                    continue;
                }

                RenderClusterList(cluster, _clusterCentroids[c], panelWidth, panelHeight);
            }

            // 남은 풀 요소는 숨긴다
            for (int i = _usedRows; i < _pool.Count; i++)
                _pool[i].Root.style.display = DisplayStyle.None;
        }

        void RenderSingle(in Entry entry)
        {
            var row = TakeRow(showName: false);

            row.Root.style.left = entry.PanelPos.x - BarWidth * 0.5f;
            row.Root.style.top = entry.PanelPos.y - 14f;

            SetFill(row, entry.HpFraction);
        }

        void RenderClusterList(List<int> cluster, Vector2 centroid, float panelWidth, float panelHeight)
        {
            // 화면 위쪽에 있는 몬스터가 리스트 위쪽 행이 되도록 정렬 (설명선 교차 최소화)
            cluster.Sort((a, b) => _entries[a].PanelPos.y.CompareTo(_entries[b].PanelPos.y));

            float listHeight = cluster.Count * RowHeight;
            float rowWidth = BarWidth + 64f;

            // 기본은 클러스터 오른쪽, 화면을 벗어나면 왼쪽으로 뒤집는다
            bool onRight = centroid.x + ListOffsetX + rowWidth < panelWidth - 4f;

            float listX;

            if (onRight)
            {
                listX = centroid.x + ListOffsetX;
            }
            else
            {
                listX = centroid.x - ListOffsetX - rowWidth;
            }

            float listY = Mathf.Clamp(centroid.y - listHeight * 0.5f, 4f, Mathf.Max(4f, panelHeight - listHeight - 4f));

            for (int i = 0; i < cluster.Count; i++)
            {
                var entry = _entries[cluster[i]];
                var row = TakeRow(showName: true);

                float rowY = listY + i * RowHeight;

                row.Root.style.left = listX;
                row.Root.style.top = rowY;
                row.Name.text = entry.Monster.Data.DisplayName;

                SetFill(row, entry.HpFraction);

                // 설명선: 행의 안쪽 끝에서 몬스터 머리 위치로
                float anchorX = onRight ? listX - 2f : listX + rowWidth + 2f;

                _lines.Add(new LeaderLine
                {
                    From = new Vector2(anchorX, rowY + RowHeight * 0.5f - 1f),
                    To = entry.PanelPos,
                });
            }
        }

        BarRow TakeRow(bool showName)
        {
            BarRow row;

            if (_usedRows < _pool.Count)
            {
                row = _pool[_usedRows];
            }
            else
            {
                row = CreateRow();
                _pool.Add(row);
            }

            _usedRows++;

            row.Root.style.display = DisplayStyle.Flex;
            row.Name.style.display = showName ? DisplayStyle.Flex : DisplayStyle.None;

            return row;
        }

        BarRow CreateRow()
        {
            var root = new VisualElement();
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;

            var name = new Label();
            name.pickingMode = PickingMode.Ignore;
            name.style.fontSize = 10;
            name.style.color = Color.white;
            name.style.width = 58f;
            name.style.unityTextAlign = TextAnchor.MiddleRight;
            name.style.marginRight = 4f;
            name.style.overflow = Overflow.Hidden;
            root.Add(name);

            var barBack = new VisualElement();
            barBack.pickingMode = PickingMode.Ignore;
            barBack.style.width = BarWidth;
            barBack.style.height = BarHeight;
            barBack.style.backgroundColor = BackColor;

            var fill = new VisualElement();
            fill.pickingMode = PickingMode.Ignore;
            fill.style.height = BarHeight;
            barBack.Add(fill);

            root.Add(barBack);

            _layer.Add(root);

            return new BarRow { Root = root, Name = name, Fill = fill };
        }

        static void SetFill(BarRow row, float fraction)
        {
            row.Fill.style.width = BarWidth * fraction;
            row.Fill.style.backgroundColor = FractionColor(fraction);
        }

        static Color FractionColor(float fraction)
        {
            // 초록(가득) -> 노랑(절반) -> 빨강(빈사)
            if (fraction > 0.5f)
                return Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.9f, 0.35f), (fraction - 0.5f) * 2f);

            return Color.Lerp(new Color(0.95f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.2f), fraction * 2f);
        }
    }
}
