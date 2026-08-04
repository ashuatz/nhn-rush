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
    ///
    /// 설명선 규칙:
    /// - 클러스터는 시드(첫 개체) 위치 기준 반경으로만 묶는다. 이동 평균 중심을 쓰면
    ///   경로를 따라 늘어선 몬스터가 연쇄 합병되어 선이 화면을 가로지르게 된다.
    /// - 행 수가 상한을 넘으면 "+N" 행으로 접는다 (선 없음).
    /// - 선은 행에서 짧은 수평 스터브를 낸 뒤 몬스터로 향하고, 끝점에 점을 찍어 어디에 닿는지 보여준다.
    /// - 리스트끼리 세로로 겹치면 아래로 밀어낸다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MonsterHealthOverlay : MonoBehaviour
    {
        /// <summary>시드에서 이 거리(패널 px) 안에 들어온 몬스터만 같은 클러스터로 묶는다.</summary>
        const float ClusterRadius = 46f;

        const int MaxRowsPerList = 7;

        const float BarWidth = 40f;
        const float BarHeight = 5f;
        const float RowHeight = 15f;
        const float RowWidth = 104f;
        const float HeadOffset = 0.75f;
        const float ListOffsetX = 58f;
        const float StubLength = 8f;

        static readonly Color BackColor = new Color(0f, 0f, 0f, 0.65f);
        static readonly Color LineColor = new Color(1f, 1f, 1f, 0.5f);

        struct Entry
        {
            public Monster Monster;
            public Vector2 PanelPos;
            public float HpFraction;
        }

        struct LeaderLine
        {
            public Vector2 Anchor;
            public Vector2 Elbow;
            public Vector2 Target;
        }

        class Cluster
        {
            public readonly List<int> Members = new List<int>(16);
            public Vector2 Seed;
            public Vector2 Centroid;
            public bool OnRight;
            public float ListX;
            public float ListY;
            public float Height;
        }

        class BarRow
        {
            public VisualElement Root;
            public Label Name;
            public VisualElement BarBack;
            public VisualElement Fill;
        }

        readonly List<Entry> _entries = new List<Entry>(64);
        readonly List<Cluster> _clusters = new List<Cluster>(24);
        readonly List<Cluster> _clusterPool = new List<Cluster>(24);
        readonly List<Cluster> _multiClusters = new List<Cluster>(24);
        readonly List<LeaderLine> _lines = new List<LeaderLine>(48);
        readonly List<BarRow> _pool = new List<BarRow>(64);

        UIDocument _doc;
        VisualElement _layer;
        VisualElement _lineLayer;
        Camera _camera;
        int _usedRows;
        bool _hiddenApplied;

        /// <summary>HP 표시 on/off. HUD 토글 버튼이 제어한다.</summary>
        public bool DisplayEnabled { get; set; } = true;

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
            painter.fillColor = LineColor;
            painter.lineWidth = 1.2f;

            foreach (var line in _lines)
            {
                painter.BeginPath();
                painter.MoveTo(line.Anchor);
                painter.LineTo(line.Elbow);
                painter.LineTo(line.Target);
                painter.Stroke();

                // 끝점 마커: 선이 어느 개체에 닿는지 보여준다
                painter.BeginPath();
                painter.Arc(line.Target, 2.2f, 0f, 360f);
                painter.Fill();
            }
        }

        void Update()
        {
            if (_layer == null)
                return;

            if (!DisplayEnabled)
            {
                HideAll();
                return;
            }

            _hiddenApplied = false;

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
            LayoutLists();
            Render();

            _lineLayer.MarkDirtyRepaint();
        }

        void HideAll()
        {
            if (_hiddenApplied)
                return;

            _hiddenApplied = true;

            foreach (var row in _pool)
                row.Root.style.display = DisplayStyle.None;

            _lines.Clear();
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

        /// <summary>시드 고정 탐욕 클러스터링. 시드에서 반경 안일 때만 합류하므로 연쇄 합병이 없다.</summary>
        void BuildClusters()
        {
            _clusters.Clear();

            for (int i = 0; i < _entries.Count; i++)
            {
                Cluster joined = null;

                foreach (var cluster in _clusters)
                {
                    if ((_entries[i].PanelPos - cluster.Seed).sqrMagnitude > ClusterRadius * ClusterRadius)
                        continue;

                    joined = cluster;
                    break;
                }

                if (joined == null)
                {
                    joined = TakeCluster();
                    joined.Seed = _entries[i].PanelPos;
                    _clusters.Add(joined);
                }

                joined.Members.Add(i);
            }

            foreach (var cluster in _clusters)
            {
                Vector2 sum = Vector2.zero;

                foreach (int index in cluster.Members)
                    sum += _entries[index].PanelPos;

                cluster.Centroid = sum / cluster.Members.Count;
            }
        }

        Cluster TakeCluster()
        {
            if (_clusterPool.Count > 0)
            {
                var cluster = _clusterPool[_clusterPool.Count - 1];
                _clusterPool.RemoveAt(_clusterPool.Count - 1);
                cluster.Members.Clear();

                return cluster;
            }

            return new Cluster();
        }

        /// <summary>다중 클러스터의 리스트 위치를 정하고, 같은 쪽 리스트끼리 겹치면 아래로 밀어낸다.</summary>
        void LayoutLists()
        {
            float panelWidth = _doc.rootVisualElement.resolvedStyle.width;
            float panelHeight = _doc.rootVisualElement.resolvedStyle.height;

            _multiClusters.Clear();

            foreach (var cluster in _clusters)
            {
                if (cluster.Members.Count < 2)
                    continue;

                // 화면 위쪽 개체가 리스트 위 행이 되도록 정렬 (선 교차 최소화)
                cluster.Members.Sort((a, b) => _entries[a].PanelPos.y.CompareTo(_entries[b].PanelPos.y));

                int rows = Mathf.Min(cluster.Members.Count, MaxRowsPerList);

                if (cluster.Members.Count > MaxRowsPerList)
                    rows++;

                cluster.Height = rows * RowHeight;
                cluster.OnRight = cluster.Centroid.x + ListOffsetX + RowWidth < panelWidth - 4f;

                if (cluster.OnRight)
                {
                    cluster.ListX = cluster.Centroid.x + ListOffsetX;
                }
                else
                {
                    cluster.ListX = cluster.Centroid.x - ListOffsetX - RowWidth;
                }

                cluster.ListY = Mathf.Clamp(cluster.Centroid.y - cluster.Height * 0.5f,
                    4f, Mathf.Max(4f, panelHeight - cluster.Height - 4f));

                _multiClusters.Add(cluster);
            }

            // 세로 겹침 해소: 위에서 아래로 훑으며 수평으로 겹치는 이전 리스트 아래로 민다
            _multiClusters.Sort((a, b) => a.ListY.CompareTo(b.ListY));

            for (int i = 1; i < _multiClusters.Count; i++)
            {
                var current = _multiClusters[i];

                for (int j = 0; j < i; j++)
                {
                    var above = _multiClusters[j];

                    bool horizontalOverlap = current.ListX < above.ListX + RowWidth + 6f
                        && above.ListX < current.ListX + RowWidth + 6f;

                    if (!horizontalOverlap)
                        continue;

                    float aboveBottom = above.ListY + above.Height;

                    if (current.ListY < aboveBottom + 2f)
                        current.ListY = aboveBottom + 2f;
                }
            }
        }

        void Render()
        {
            _usedRows = 0;
            _lines.Clear();

            foreach (var cluster in _clusters)
            {
                if (cluster.Members.Count == 1)
                    RenderSingle(_entries[cluster.Members[0]]);
            }

            foreach (var cluster in _multiClusters)
                RenderClusterList(cluster);

            // 남은 풀 요소는 숨긴다
            for (int i = _usedRows; i < _pool.Count; i++)
                _pool[i].Root.style.display = DisplayStyle.None;

            // 클러스터 객체 회수
            foreach (var cluster in _clusters)
                _clusterPool.Add(cluster);
        }

        void RenderSingle(in Entry entry)
        {
            var row = TakeRow(showName: false, showBar: true);

            row.Root.style.left = entry.PanelPos.x - BarWidth * 0.5f;
            row.Root.style.top = entry.PanelPos.y - 14f;

            SetFill(row, entry.HpFraction);
        }

        void RenderClusterList(Cluster cluster)
        {
            int shown = Mathf.Min(cluster.Members.Count, MaxRowsPerList);

            for (int i = 0; i < shown; i++)
            {
                var entry = _entries[cluster.Members[i]];
                var row = TakeRow(showName: true, showBar: true);

                float rowY = cluster.ListY + i * RowHeight;

                row.Root.style.left = cluster.ListX;
                row.Root.style.top = rowY;
                row.Name.text = entry.Monster.Data.DisplayName;

                SetFill(row, entry.HpFraction);

                // 설명선: 행 가장자리에서 짧은 스터브를 낸 뒤 몬스터 머리로
                float rowCenterY = rowY + RowHeight * 0.5f - 1f;
                float anchorX;
                float elbowX;

                if (cluster.OnRight)
                {
                    anchorX = cluster.ListX - 2f;
                    elbowX = anchorX - StubLength;
                }
                else
                {
                    anchorX = cluster.ListX + RowWidth + 2f;
                    elbowX = anchorX + StubLength;
                }

                _lines.Add(new LeaderLine
                {
                    Anchor = new Vector2(anchorX, rowCenterY),
                    Elbow = new Vector2(elbowX, rowCenterY),
                    Target = entry.PanelPos,
                });
            }

            // 넘친 개체는 "+N" 행으로 접는다 (선 없음)
            if (cluster.Members.Count > MaxRowsPerList)
            {
                var row = TakeRow(showName: true, showBar: false);

                row.Root.style.left = cluster.ListX;
                row.Root.style.top = cluster.ListY + shown * RowHeight;
                row.Name.text = $"+{cluster.Members.Count - MaxRowsPerList}";
            }
        }

        BarRow TakeRow(bool showName, bool showBar)
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
            row.BarBack.style.display = showBar ? DisplayStyle.Flex : DisplayStyle.None;

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

            return new BarRow { Root = root, Name = name, BarBack = barBack, Fill = fill };
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
