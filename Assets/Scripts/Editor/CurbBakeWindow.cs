using Rush.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.EditorTools
{
    /// <summary>
    /// 씬에 있는 연석 경로를 한 곳에서 확인하고 일괄 베이크하는 창.
    /// 개별 편집은 CurbPath 인스펙터에서 한다.
    /// </summary>
    public class CurbBakeWindow : EditorWindow
    {
        static readonly Color windowBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color panelBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color panelBorderColor = new Color(0.17f, 0.17f, 0.17f, 1f);
        static readonly Color accentColor = new Color(0.36f, 0.36f, 0.36f, 1f);
        static readonly Color subtleTextColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        static readonly Color warnTextColor = new Color(1f, 0.6f, 0.2f, 1f);

        const float DefaultSegmentLength = 5f;

        ScrollView _listBody;
        Label _statusLabel;

        [MenuItem("Rush/Curb Baker")]
        public static void Open()
        {
            var window = GetWindow<CurbBakeWindow>();
            window.titleContent = new GUIContent("Curb Baker");
            window.minSize = new Vector2(380f, 320f);
        }

        void OnEnable()
        {
            EditorApplication.hierarchyChanged += RefreshList;
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= RefreshList;
        }

        void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.backgroundColor = windowBackground;
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 10f;
            root.style.paddingBottom = 10f;

            VisualElement actionBody;
            root.Add(CreateSectionShell("연석 경로", "Curb", out actionBody));
            BuildActions(actionBody);

            VisualElement listShellBody;
            VisualElement listShell = CreateSectionShell("씬의 경로", string.Empty, out listShellBody);
            listShell.style.flexGrow = 1f;
            root.Add(listShell);

            _listBody = new ScrollView(ScrollViewMode.Vertical);
            _listBody.style.flexGrow = 1f;
            listShellBody.Add(_listBody);

            _statusLabel = new Label(string.Empty);
            _statusLabel.style.color = subtleTextColor;
            _statusLabel.style.marginTop = 6f;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_statusLabel);

            RefreshList();
        }

        void BuildActions(VisualElement body)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            row.Add(CreateButton("새 경로 만들기", CreateNewPath, 1f));
            row.Add(CreateButton("전체 프리뷰", RefreshAllPreviews, 1f));
            row.Add(CreateButton("전체 Bake", BakeAll, 1f));

            body.Add(row);

            var hint = new Label("경로를 만든 뒤 조각 FBX를 middle에 넣고, 필요하면 startCap / endCap으로 끝을 마감한다.");
            hint.style.color = subtleTextColor;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 8f;
            body.Add(hint);
        }

        void RefreshList()
        {
            if (_listBody == null)
                return;

            _listBody.Clear();

            var paths = Object.FindObjectsByType<CurbPath>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (paths.Length == 0)
            {
                var empty = new Label("씬에 CurbPath가 없다. '새 경로 만들기'로 시작하라.");
                empty.style.color = subtleTextColor;
                empty.style.whiteSpace = WhiteSpace.Normal;
                _listBody.Add(empty);
                return;
            }

            foreach (CurbPath path in paths)
                _listBody.Add(CreateRow(path));
        }

        VisualElement CreateRow(CurbPath path)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;

            var name = new Label(path.gameObject.name);
            name.style.color = Color.white;
            name.style.flexGrow = 1f;
            row.Add(name);

            var info = new Label(DescribePath(path));
            info.style.color = subtleTextColor;
            info.style.marginRight = 8f;
            row.Add(info);

            row.Add(CreateButton("선택", () => Select(path), 0f));
            row.Add(CreateButton("프리뷰", () => RefreshPreview(path), 0f));
            row.Add(CreateButton("Bake", () => Bake(path), 0f));

            return row;
        }

        static string DescribePath(CurbPath path)
        {
            if (!CurbBakeUtility.TryGetStats(path, out CurbMeshBuilder.BuildStats stats, out string message))
                return "생성 불가";

            return $"{stats.pathLength:0.#}m / 조각 {stats.pieceCount} / 버텍스 {stats.vertexCount}";
        }

        void Select(CurbPath path)
        {
            Selection.activeGameObject = path.gameObject;
            SceneView.FrameLastActiveSceneView();
        }

        void RefreshPreview(CurbPath path)
        {
            if (!CurbBakeUtility.RebuildPreview(path, CurbPreviewMode.ActiveCreate, out CurbMeshBuilder.BuildStats stats, out string message))
            {
                SetStatus($"{path.gameObject.name}: {message}", true);
                return;
            }

            SetStatus($"{path.gameObject.name} 프리뷰 갱신 (조각 {stats.pieceCount})", false);
        }

        void Bake(CurbPath path)
        {
            if (!CurbBakeUtility.Bake(path, out CurbMeshBuilder.BuildStats stats, out string message))
            {
                SetStatus($"{path.gameObject.name}: {message}", true);
                return;
            }

            SetStatus($"{path.gameObject.name} 베이크 완료 (버텍스 {stats.vertexCount})", false);
            RefreshList();
        }

        void RefreshAllPreviews()
        {
            var paths = Object.FindObjectsByType<CurbPath>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (CurbPath path in paths)
                CurbBakeUtility.RebuildPreview(path, CurbPreviewMode.ActiveCreate, out _, out _);

            SetStatus($"{paths.Length}개 프리뷰 갱신", false);
            RefreshList();
        }

        void BakeAll()
        {
            var paths = Object.FindObjectsByType<CurbPath>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int done = 0;

            foreach (CurbPath path in paths)
            {
                if (CurbBakeUtility.Bake(path, out _, out _))
                    done++;
            }

            SetStatus($"{done}/{paths.Length}개 베이크 완료", done != paths.Length);
            RefreshList();
        }

        void CreateNewPath()
        {
            var go = new GameObject("Curb");
            Undo.RegisterCreatedObjectUndo(go, "연석 경로 생성");

            go.transform.position = GuessSpawnPosition();

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.widthMultiplier = 0.05f;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, new Vector3(DefaultSegmentLength, 0f, 0f));
            line.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Line.mat");

            var path = go.AddComponent<CurbPath>();
            path.sourceLine = line;

            Selection.activeGameObject = go;
            SetStatus("새 경로를 만들었다. 인스펙터에서 Ctrl+클릭으로 점을 이어 붙여라.", false);
            RefreshList();
        }

        static Vector3 GuessSpawnPosition()
        {
            SceneView view = SceneView.lastActiveSceneView;

            if (view == null)
                return Vector3.zero;

            return view.pivot;
        }

        void SetStatus(string message, bool warning)
        {
            if (_statusLabel == null)
                return;

            _statusLabel.text = message;

            if (warning)
            {
                _statusLabel.style.color = warnTextColor;
                return;
            }

            _statusLabel.style.color = subtleTextColor;
        }

        static Button CreateButton(string text, System.Action action, float grow)
        {
            var button = new Button(action);
            button.text = text;
            button.style.flexGrow = grow;
            button.style.marginLeft = 2f;
            button.style.marginRight = 2f;
            button.style.height = 22f;

            return button;
        }

        static VisualElement CreateSectionShell(string title, string badge, out VisualElement bodyContainer)
        {
            var shell = new VisualElement();
            shell.style.flexDirection = FlexDirection.Column;
            shell.style.backgroundColor = panelBackground;
            shell.style.overflow = Overflow.Hidden;
            shell.style.marginBottom = 10f;

            shell.style.borderLeftWidth = 1f;
            shell.style.borderRightWidth = 1f;
            shell.style.borderTopWidth = 1f;
            shell.style.borderBottomWidth = 1f;
            shell.style.borderLeftColor = panelBorderColor;
            shell.style.borderRightColor = panelBorderColor;
            shell.style.borderTopColor = panelBorderColor;
            shell.style.borderBottomColor = panelBorderColor;

            shell.Add(CreateSectionHeader(title, badge));
            shell.Add(CreateSectionAccentBar());

            bodyContainer = new VisualElement();
            bodyContainer.style.flexGrow = 1f;
            bodyContainer.style.flexDirection = FlexDirection.Column;
            bodyContainer.style.paddingLeft = 14f;
            bodyContainer.style.paddingRight = 14f;
            bodyContainer.style.paddingTop = 12f;
            bodyContainer.style.paddingBottom = 14f;
            bodyContainer.style.backgroundColor = panelBackground;

            shell.Add(bodyContainer);

            return shell;
        }

        static VisualElement CreateSectionHeader(string title, string badge)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.paddingLeft = 14f;
            header.style.paddingRight = 14f;
            header.style.paddingTop = 8f;
            header.style.paddingBottom = 8f;

            var titleLabel = new Label(title);
            titleLabel.style.color = Color.white;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(titleLabel);

            if (!string.IsNullOrEmpty(badge))
            {
                var badgeLabel = new Label(badge);
                badgeLabel.style.color = subtleTextColor;
                header.Add(badgeLabel);
            }

            return header;
        }

        static VisualElement CreateSectionAccentBar()
        {
            var bar = new VisualElement();
            bar.style.height = 2f;
            bar.style.backgroundColor = accentColor;

            return bar;
        }
    }
}
