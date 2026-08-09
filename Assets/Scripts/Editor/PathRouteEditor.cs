using Rush.Stage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rush.EditorTools
{
    /// <summary>
    /// PathRoute 인스펙터. 경로 양끝에 웨이포인트를 붙이는 버튼을 준다.
    /// 새 점은 끝 구간의 방향과 길이를 그대로 연장한 자리에 놓아 경로가 꺾이지 않는다.
    /// 이름은 추가 후 P00부터 다시 매긴다 (앞에 끼워 넣으면 뒤 번호가 전부 밀린다).
    /// </summary>
    [CustomEditor(typeof(PathRoute))]
    public class PathRouteEditor : Editor
    {
        /// <summary>연장할 방향을 구할 수 없을 때(점이 1개거나 두 점이 겹칠 때) 쓰는 간격.</summary>
        const float FallbackStep = 2f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var route = target as PathRoute;

            if (route == null)
                return;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("웨이포인트", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"현재 {route.transform.childCount}개 · 길이 {route.TotalLength:F1}");

            if (IsSceneOverrideEdit(route))
            {
                EditorGUILayout.HelpBox(
                    "이 경로는 씬에 놓인 프리팹 인스턴스다. 여기서 추가하면 프리팹이 아니라 씬 오버라이드로 남는다.\n"
                    + "Paths.prefab을 열고 편집할 것.", MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("맨 앞에 점 추가"))
                    AddPoint(route, atFront: true);

                if (GUILayout.Button("맨 뒤에 점 추가"))
                    AddPoint(route, atFront: false);
            }

            EditorGUILayout.HelpBox("추가한 점은 끝 구간을 그대로 연장한 위치에 놓인다. 씬에서 끌어 다듬을 것.",
                MessageType.None);
        }

        /// <summary>
        /// 프리팹 인스턴스를 씬에서 직접 고치는 상황인지. 프리팹 스테이지 안에서 편집하는 것은 정상이다.
        /// </summary>
        static bool IsSceneOverrideEdit(PathRoute route)
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                return false;

            return PrefabUtility.IsPartOfPrefabInstance(route.gameObject);
        }

        static void AddPoint(PathRoute route, bool atFront)
        {
            string undoName = atFront ? "경로 맨 앞에 점 추가" : "경로 맨 뒤에 점 추가";
            var parent = route.transform;

            Vector3 position = ResolveNewPosition(parent, atFront);

            var point = new GameObject("P");

            // 프리팹 스테이지에서 눌렀으면 새 오브젝트도 그 스테이지에 들어가야 한다
            UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(point);

            Undo.RegisterCreatedObjectUndo(point, undoName);
            Undo.SetTransformParent(point.transform, parent, undoName);

            point.transform.position = position;
            point.transform.localRotation = Quaternion.identity;
            point.transform.localScale = Vector3.one;

            if (atFront)
                point.transform.SetAsFirstSibling();

            RenumberPoints(route, undoName);

            route.CachePoints();

            EditorUtility.SetDirty(route);
            EditorSceneManager.MarkSceneDirty(route.gameObject.scene);

            // 선택은 경로에 그대로 둔다. 새 점으로 옮기면 버튼이 사라져 연속으로 못 누른다.
            EditorGUIUtility.PingObject(point);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 새 점의 위치. 끝 구간(앞이면 P00-P01, 뒤면 마지막 두 점)의 벡터를 그대로 밖으로 한 칸 더 잇는다.
        /// 점이 부족하거나 두 점이 겹쳐 방향을 못 구하면 기본 간격만큼 떨어뜨린다.
        /// </summary>
        static Vector3 ResolveNewPosition(Transform parent, bool atFront)
        {
            int count = parent.childCount;

            if (count == 0)
                return parent.position;

            Transform edge = atFront ? parent.GetChild(0) : parent.GetChild(count - 1);

            if (count == 1)
                return edge.position + FallbackDirection(parent, atFront) * FallbackStep;

            Transform inner = atFront ? parent.GetChild(1) : parent.GetChild(count - 2);

            Vector3 outward = edge.position - inner.position;

            if (outward.sqrMagnitude < 0.0001f)
                return edge.position + FallbackDirection(parent, atFront) * FallbackStep;

            return edge.position + outward;
        }

        static Vector3 FallbackDirection(Transform parent, bool atFront)
        {
            Vector3 forward = parent.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            forward.Normalize();

            if (atFront)
                return -forward;

            return forward;
        }

        /// <summary>웨이포인트 이름을 P00부터 다시 매긴다. 셋업의 씬 정리와 같은 규칙이다.</summary>
        static void RenumberPoints(PathRoute route, string undoName)
        {
            var parent = route.transform;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                string expected = $"P{i:00}";

                if (child.name == expected)
                    continue;

                Undo.RecordObject(child.gameObject, undoName);
                child.name = expected;
            }
        }
    }
}
