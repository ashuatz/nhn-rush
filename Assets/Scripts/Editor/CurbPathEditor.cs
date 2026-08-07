using Rush.Art;
using UnityEditor;
using UnityEngine;

namespace Rush.EditorTools
{
    /// <summary>
    /// CurbPath 인스펙터. 씬에서 경로 포인트를 직접 편집하고 편집 즉시 프리뷰 메시를 갱신한다.
    /// 실제 생성/저장은 CurbBakeUtility가 처리한다.
    /// </summary>
    [CustomEditor(typeof(CurbPath))]
    public class CurbPathEditor : Editor
    {
        static readonly Color pathColor = new Color(0.4f, 0.8f, 1f, 1f);
        static readonly Color pointColor = new Color(1f, 0.85f, 0.3f, 1f);
        static readonly Color removeColor = new Color(0.9f, 0.35f, 0.3f, 1f);

        CurbMeshBuilder.BuildStats _stats;
        string _message = string.Empty;
        bool _buildFailed;

        void OnEnable()
        {
            SceneView.duringSceneGui += DrawSceneTools;
            Undo.undoRedoPerformed += OnUndoRedo;
            Refresh(CurbPreviewMode.Passive);
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= DrawSceneTools;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo()
        {
            // 되돌린 경로와 메시가 어긋나지 않게 다시 굽는다.
            Refresh(CurbPreviewMode.Passive);
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            bool changed = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();

            if (changed)
                Refresh(CurbPreviewMode.Active);

            EditorGUILayout.Space(6f);
            DrawPathTools();

            EditorGUILayout.Space(6f);
            DrawRandomTools();

            EditorGUILayout.Space(6f);
            DrawBakeTools();

            EditorGUILayout.Space(6f);
            DrawStatus();
        }

        void DrawPathTools()
        {
            var path = target as CurbPath;

            EditorGUILayout.LabelField("경로 편집", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("씬에서 노란 점을 끌어 이동, Ctrl+클릭으로 끝에 점 추가, 위쪽 빨간 점을 클릭하면 삭제.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("점 추가(마지막 뒤)"))
                    AppendPointAtEnd(path);

                if (GUILayout.Button("바닥에 스냅"))
                    SnapPointsToGround(path);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(path.sourceLine == null))
                {
                    if (GUILayout.Button("라인 -> 자체 포인트 복사"))
                        CopyLineToPoints(path);
                }

                if (GUILayout.Button("자체 포인트 -> 라인 반영"))
                    PushPointsToLine(path);
            }
        }

        void DrawRandomTools()
        {
            var path = target as CurbPath;

            EditorGUILayout.LabelField("랜덤", EditorStyles.boldLabel);

            if (path.middles != null && path.middles.Count > 1)
                EditorGUILayout.HelpBox($"중간 조각 {path.middles.Count}종을 랜덤으로 섞어 배치한다.", MessageType.None);

            if (!GUILayout.Button("시드 다시 뽑기"))
                return;

            Undo.RecordObject(path, "커브 랜덤 시드 변경");
            path.randomSeed = Random.Range(1, int.MaxValue);
            EditorUtility.SetDirty(path);
            Refresh(CurbPreviewMode.Active);
        }

        void DrawBakeTools()
        {
            var path = target as CurbPath;

            EditorGUILayout.LabelField("베이크", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("프리뷰 갱신"))
                    Refresh(CurbPreviewMode.ActiveCreate);

                if (GUILayout.Button("Bake (메시 에셋 저장)"))
                    BakeSelected(path);
            }

            if (GUILayout.Button("생성 오브젝트 삭제"))
                CurbBakeUtility.ClearOutput(path);

            if (path.output == null)
                EditorGUILayout.HelpBox("아직 출력 오브젝트가 없다. '프리뷰 갱신'을 누르면 자식 CurbMesh를 만들고 그 뒤로는 편집이 바로 반영된다.", MessageType.Info);

            if (path.FirstMiddle == null)
                EditorGUILayout.HelpBox("중간 조각 메시가 비어 있어 임시 폴백 단면을 쓰고 있다. ProBuilder FBX를 middles에 넣어라.", MessageType.Info);

            if (CurbBakeUtility.IsPreviewOnly(path))
                EditorGUILayout.HelpBox("현재 메시는 저장되지 않는 프리뷰다. Bake를 눌러 에셋으로 남겨라.", MessageType.Warning);
        }

        void DrawStatus()
        {
            if (_buildFailed)
            {
                EditorGUILayout.HelpBox(_message, MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("경로 길이", $"{_stats.pathLength:0.###} m");
            EditorGUILayout.LabelField("조각 수", _stats.pieceCount.ToString());
            EditorGUILayout.LabelField("버텍스 / 삼각형", $"{_stats.vertexCount} / {_stats.triangleCount}");

            if (!string.IsNullOrEmpty(_message))
                EditorGUILayout.HelpBox(_message, MessageType.Warning);
        }

        void Refresh(CurbPreviewMode mode)
        {
            var path = target as CurbPath;

            if (path == null)
                return;

            _buildFailed = !CurbBakeUtility.RebuildPreview(path, mode, out _stats, out _message);
        }

        void BakeSelected(CurbPath path)
        {
            _buildFailed = !CurbBakeUtility.Bake(path, out _stats, out _message);
        }

        void DrawSceneTools(SceneView view)
        {
            var path = target as CurbPath;

            if (path == null)
                return;

            if (targets.Length > 1)
                return;

            if (EditorUtility.IsPersistent(path))
                return;

            DrawPathLine(path);
            DrawPointHandles(path);
            HandleCtrlClickAppend(path);
        }

        void DrawPathLine(CurbPath path)
        {
            int count = path.PointCount;

            if (count < 2)
                return;

            Transform owner = path.transform;
            var line = new Vector3[count];

            for (int i = 0; i < count; i++)
                line[i] = owner.TransformPoint(path.GetPoint(i));

            Handles.color = pathColor;
            Handles.DrawAAPolyLine(2f, line);

            if (path.IsClosed)
                Handles.DrawAAPolyLine(2f, line[count - 1], line[0]);
        }

        void DrawPointHandles(CurbPath path)
        {
            Transform owner = path.transform;
            int count = path.PointCount;
            int removeIndex = -1;

            for (int i = 0; i < count; i++)
            {
                Vector3 world = owner.TransformPoint(path.GetPoint(i));
                float size = HandleUtility.GetHandleSize(world) * 0.08f;

                Handles.color = pointColor;

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);

                if (EditorGUI.EndChangeCheck())
                {
                    RecordPathUndo(path, "커브 포인트 이동");
                    path.SetPoint(i, owner.InverseTransformPoint(moved));
                    MarkPathDirty(path);
                    Refresh(CurbPreviewMode.Active);
                }

                // 점이 2개뿐이면 삭제 핸들을 숨긴다.
                if (count <= 2)
                    continue;

                Handles.color = removeColor;
                Vector3 removeHandle = world + Vector3.up * size * 3f;

                if (Handles.Button(removeHandle, Quaternion.identity, size * 0.5f, size * 0.7f, Handles.DotHandleCap))
                    removeIndex = i;
            }

            if (removeIndex < 0)
                return;

            RecordPathUndo(path, "커브 포인트 삭제");
            path.RemovePoint(removeIndex);
            MarkPathDirty(path);
            Refresh(CurbPreviewMode.Active);
        }

        void HandleCtrlClickAppend(CurbPath path)
        {
            Event current = Event.current;

            if (current.type != EventType.MouseDown)
                return;

            if (current.button != 0 || !current.control)
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);

            if (!TryPickSurface(path, ray, out Vector3 hit))
                return;

            RecordPathUndo(path, "커브 포인트 추가");
            path.AddPoint(path.transform.InverseTransformPoint(hit));
            MarkPathDirty(path);
            Refresh(CurbPreviewMode.Active);

            current.Use();
        }

        bool TryPickSurface(CurbPath path, Ray ray, out Vector3 hit)
        {
            // 씬 지오메트리 스냅. 콜라이더가 없어도 동작한다.
            if (TryRaySnap(ray, out hit))
                return true;

            if (Physics.Raycast(ray, out RaycastHit physicsHit, 1000f))
            {
                hit = physicsHit.point;
                return true;
            }

            // 마지막 포인트 높이의 수평면에 투영
            float height = path.transform.position.y;

            if (path.PointCount > 0)
                height = path.transform.TransformPoint(path.GetPoint(path.PointCount - 1)).y;

            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));

            if (plane.Raycast(ray, out float distance))
            {
                hit = ray.GetPoint(distance);
                return true;
            }

            hit = Vector3.zero;
            return false;
        }

        static bool TryRaySnap(Ray ray, out Vector3 point)
        {
            object snapped = HandleUtility.RaySnap(ray);

            if (snapped is RaycastHit snapHit)
            {
                point = snapHit.point;
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        void AppendPointAtEnd(CurbPath path)
        {
            Vector3 next = Vector3.zero;
            int count = path.PointCount;

            if (count == 1)
                next = path.GetPoint(0) + Vector3.right;

            if (count >= 2)
            {
                Vector3 last = path.GetPoint(count - 1);
                Vector3 previous = path.GetPoint(count - 2);
                next = last + (last - previous);
            }

            RecordPathUndo(path, "커브 포인트 추가");
            path.AddPoint(next);
            MarkPathDirty(path);
            Refresh(CurbPreviewMode.Active);
        }

        void SnapPointsToGround(CurbPath path)
        {
            int count = path.PointCount;

            if (count == 0)
                return;

            RecordPathUndo(path, "커브 포인트 바닥 스냅");

            Transform owner = path.transform;

            for (int i = 0; i < count; i++)
            {
                Vector3 world = owner.TransformPoint(path.GetPoint(i));
                var ray = new Ray(world + Vector3.up * 20f, Vector3.down);

                if (!TryPickGround(ray, out Vector3 ground))
                    continue;

                path.SetPoint(i, owner.InverseTransformPoint(ground));
            }

            MarkPathDirty(path);
            Refresh(CurbPreviewMode.Active);
        }

        static bool TryPickGround(Ray ray, out Vector3 ground)
        {
            if (Physics.Raycast(ray, out RaycastHit physicsHit, 200f))
            {
                ground = physicsHit.point;
                return true;
            }

            return TryRaySnap(ray, out ground);
        }

        void CopyLineToPoints(CurbPath path)
        {
            Undo.RecordObject(path, "라인 포인트 복사");
            path.CopyLineToPoints();
            EditorUtility.SetDirty(path);
        }

        void PushPointsToLine(CurbPath path)
        {
            LineRenderer line = path.sourceLine;

            if (line == null)
                line = path.GetComponent<LineRenderer>();

            if (line == null)
            {
                _message = "반영할 LineRenderer가 없다.";
                return;
            }

            Undo.RecordObject(line, "라인에 포인트 반영");
            path.ApplyPointsToLine(line);
            EditorUtility.SetDirty(line);
        }

        static void RecordPathUndo(CurbPath path, string label)
        {
            if (path.sourceLine != null)
            {
                Undo.RecordObject(path.sourceLine, label);
                return;
            }

            Undo.RecordObject(path, label);
        }

        static void MarkPathDirty(CurbPath path)
        {
            if (path.sourceLine != null)
            {
                EditorUtility.SetDirty(path.sourceLine);
                return;
            }

            EditorUtility.SetDirty(path);
        }
    }
}
