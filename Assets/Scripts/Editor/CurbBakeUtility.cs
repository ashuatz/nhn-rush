using System.IO;
using System.Text;
using Rush.Art;
using UnityEditor;
using UnityEngine;

namespace Rush.EditorTools
{
    /// <summary>프리뷰 갱신 강도.</summary>
    public enum CurbPreviewMode
    {
        /// <summary>이미 프리뷰가 붙어 있을 때만 갱신한다. 베이크된 에셋은 건드리지 않는다.</summary>
        Passive,

        /// <summary>편집 중이라 즉시 반영한다. 베이크된 에셋 대신 프리뷰를 붙인다.</summary>
        Active,

        /// <summary>Active와 같고, 출력 오브젝트가 없으면 만든다.</summary>
        ActiveCreate,
    }

    /// <summary>
    /// CurbPath의 프리뷰/베이크 처리. 인스펙터와 베이크 윈도우가 같은 경로를 쓰도록 여기 모아 둔다.
    /// 프리뷰 메시는 저장되지 않고, Bake만 Assets/RushGame/Generated에 메시 에셋을 남긴다.
    /// </summary>
    public static class CurbBakeUtility
    {
        public const string GeneratedFolder = "Assets/RushGame/Generated";
        public const string OutputChildName = "CurbMesh";

        const string PreviewMeshName = "CurbPreview";

        static Mesh _scratchMesh;

        /// <summary>
        /// 프리뷰 메시를 다시 굽는다. 출력 오브젝트가 없거나 갱신 대상이 아니면 통계만 계산한다.
        /// </summary>
        public static bool RebuildPreview(CurbPath path, CurbPreviewMode mode,
            out CurbMeshBuilder.BuildStats stats, out string message)
        {
            stats = default;
            message = string.Empty;

            if (path == null)
                return false;

            // 프리팹 에셋을 선택만 한 상태에서는 오브젝트를 만들지 않는다.
            if (EditorUtility.IsPersistent(path))
                return false;

            MeshFilter filter = path.output;

            if (filter == null && mode == CurbPreviewMode.ActiveCreate)
                filter = EnsureOutput(path);

            if (filter == null)
                return CurbMeshBuilder.Build(path, ScratchMesh, out stats, out message);

            // 저장된 베이크 결과는 실제 편집이나 명시적 요청이 있을 때만 프리뷰로 교체한다.
            if (mode == CurbPreviewMode.Passive && filter.sharedMesh != null && AssetDatabase.Contains(filter.sharedMesh))
                return CurbMeshBuilder.Build(path, ScratchMesh, out stats, out message);

            Mesh preview = GetOrCreatePreviewMesh(filter);
            bool built = CurbMeshBuilder.Build(path, preview, out stats, out message);

            if (!built)
                preview.Clear();

            filter.sharedMesh = preview;
            ApplyMaterials(path, filter);
            SceneView.RepaintAll();

            return built;
        }

        /// <summary>씬을 건드리지 않고 결과 통계만 계산한다. 목록 표시용.</summary>
        public static bool TryGetStats(CurbPath path, out CurbMeshBuilder.BuildStats stats, out string message)
        {
            stats = default;
            message = string.Empty;

            if (path == null)
                return false;

            return CurbMeshBuilder.Build(path, ScratchMesh, out stats, out message);
        }

        /// <summary>메시를 굽고 에셋으로 저장한 뒤 출력 오브젝트에 물린다.</summary>
        public static bool Bake(CurbPath path, out CurbMeshBuilder.BuildStats stats, out string message)
        {
            stats = default;
            message = string.Empty;

            if (path == null)
                return false;

            if (EditorUtility.IsPersistent(path))
            {
                message = "프리팹 에셋 상태에서는 베이크할 수 없다. 씬에 올린 뒤 실행하라.";
                return false;
            }

            var baked = new Mesh();

            if (!CurbMeshBuilder.Build(path, baked, out stats, out message))
            {
                Object.DestroyImmediate(baked);
                return false;
            }

            EnsureGeneratedFolder();

            string assetPath = ResolveAssetPath(path);
            baked.name = Path.GetFileNameWithoutExtension(assetPath);
            Mesh stored = StoreMeshAsset(baked, assetPath);

            MeshFilter filter = EnsureOutput(path);
            Mesh previous = filter.sharedMesh;

            Undo.RecordObject(filter, "커브 메시 베이크");
            filter.sharedMesh = stored;
            ApplyMaterials(path, filter);
            EditorUtility.SetDirty(filter);

            DestroyPreviewMesh(previous, stored);

            Debug.Log($"[Curb] 베이크 완료: {assetPath} (조각 {stats.pieceCount}개, 버텍스 {stats.vertexCount})", filter);

            return true;
        }

        /// <summary>출력 자식 오브젝트를 찾거나 새로 만든다.</summary>
        public static MeshFilter EnsureOutput(CurbPath path)
        {
            if (path.output != null)
                return path.output;

            Transform existing = path.transform.Find(OutputChildName);

            if (existing != null)
            {
                var found = existing.GetComponent<MeshFilter>();

                if (found != null)
                {
                    Undo.RecordObject(path, "커브 출력 연결");
                    path.output = found;
                    EditorUtility.SetDirty(path);
                    return found;
                }
            }

            var child = new GameObject(OutputChildName);
            Undo.RegisterCreatedObjectUndo(child, "커브 출력 생성");

            child.transform.SetParent(path.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;

            MeshFilter filter = child.AddComponent<MeshFilter>();
            child.AddComponent<MeshRenderer>();

            Undo.RecordObject(path, "커브 출력 연결");
            path.output = filter;
            EditorUtility.SetDirty(path);

            return filter;
        }

        /// <summary>출력 오브젝트를 제거한다. 베이크된 메시 에셋은 남는다.</summary>
        public static void ClearOutput(CurbPath path)
        {
            if (path == null || path.output == null)
                return;

            GameObject go = path.output.gameObject;
            Mesh preview = path.output.sharedMesh;

            Undo.RecordObject(path, "커브 출력 해제");
            path.output = null;
            EditorUtility.SetDirty(path);

            Undo.DestroyObjectImmediate(go);
            DestroyPreviewMesh(preview, null);
        }

        /// <summary>현재 붙어 있는 메시가 저장되지 않는 프리뷰인지.</summary>
        public static bool IsPreviewOnly(CurbPath path)
        {
            if (path == null || path.output == null)
                return false;

            Mesh mesh = path.output.sharedMesh;

            if (mesh == null)
                return false;

            return !AssetDatabase.Contains(mesh);
        }

        /// <summary>출력 오브젝트가 없을 때 통계만 뽑기 위한 임시 메시.</summary>
        static Mesh ScratchMesh
        {
            get
            {
                if (_scratchMesh != null)
                    return _scratchMesh;

                _scratchMesh = new Mesh();
                _scratchMesh.name = "CurbScratch";
                _scratchMesh.hideFlags = HideFlags.HideAndDontSave;

                return _scratchMesh;
            }
        }

        static Mesh GetOrCreatePreviewMesh(MeshFilter filter)
        {
            Mesh current = filter.sharedMesh;

            // 이미 프리뷰 메시면 재사용해서 임시 메시가 계속 쌓이는 걸 막는다.
            if (current != null && current.name == PreviewMeshName && !AssetDatabase.Contains(current))
                return current;

            var preview = new Mesh();
            preview.name = PreviewMeshName;
            preview.hideFlags = HideFlags.DontSave;

            return preview;
        }

        /// <summary>
        /// 저장할 에셋 경로를 정한다. 이미 이 경로가 쓰던 에셋이 있으면 그대로 덮고,
        /// 없으면 오브젝트 이름 기반 경로를 쓰되 다른 경로가 선점했으면 고유 이름을 만든다.
        /// </summary>
        static string ResolveAssetPath(CurbPath path)
        {
            Mesh current = null;

            if (path.output != null)
                current = path.output.sharedMesh;

            if (current != null)
            {
                string currentPath = AssetDatabase.GetAssetPath(current);

                if (!string.IsNullOrEmpty(currentPath) && currentPath.StartsWith(GeneratedFolder))
                    return currentPath;
            }

            string candidate = $"{GeneratedFolder}/Curb_{SanitizeName(path.gameObject.name)}.asset";

            // 같은 이름의 다른 연석이 이미 쓰고 있으면 덮어쓰지 않는다.
            if (AssetDatabase.LoadAssetAtPath<Mesh>(candidate) != null)
                return AssetDatabase.GenerateUniqueAssetPath(candidate);

            return candidate;
        }

        /// <summary>같은 경로에 이미 에셋이 있으면 GUID를 유지하도록 내용만 갈아끼운다.</summary>
        static Mesh StoreMeshAsset(Mesh baked, string assetPath)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

            if (existing == null)
            {
                AssetDatabase.CreateAsset(baked, assetPath);
                AssetDatabase.SaveAssets();
                return baked;
            }

            Undo.RecordObject(existing, "커브 메시 갱신");
            EditorUtility.CopySerialized(baked, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(baked);
            AssetDatabase.SaveAssets();

            return existing;
        }

        /// <summary>더 이상 쓰이지 않는 임시 프리뷰 메시를 정리한다.</summary>
        static void DestroyPreviewMesh(Mesh mesh, Mesh replacement)
        {
            if (mesh == null || mesh == replacement)
                return;

            if (mesh.name != PreviewMeshName || AssetDatabase.Contains(mesh))
                return;

            Object.DestroyImmediate(mesh);
        }

        static void ApplyMaterials(CurbPath path, MeshFilter filter)
        {
            var renderer = filter.GetComponent<MeshRenderer>();

            if (renderer == null)
                return;

            if (path.materials != null && path.materials.Length > 0)
            {
                Undo.RecordObject(renderer, "커브 머티리얼 적용");
                renderer.sharedMaterials = path.materials;
                EditorUtility.SetDirty(renderer);
                return;
            }

            // 지정이 없으면 조각 FBX의 머티리얼을 가져와 채워 준다.
            Material[] resolved = ResolveMaterialsFromSource(path.middle);

            if (resolved == null || resolved.Length == 0)
                return;

            Undo.RecordObject(path, "커브 머티리얼 적용");
            path.materials = resolved;
            EditorUtility.SetDirty(path);

            Undo.RecordObject(renderer, "커브 머티리얼 적용");
            renderer.sharedMaterials = resolved;
            EditorUtility.SetDirty(renderer);
        }

        static Material[] ResolveMaterialsFromSource(Mesh mesh)
        {
            if (mesh == null)
                return null;

            string assetPath = AssetDatabase.GetAssetPath(mesh);

            if (string.IsNullOrEmpty(assetPath))
                return null;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (model == null)
                return null;

            var renderer = model.GetComponentInChildren<MeshRenderer>();

            if (renderer == null)
                return null;

            return renderer.sharedMaterials;
        }

        static void EnsureGeneratedFolder()
        {
            if (AssetDatabase.IsValidFolder(GeneratedFolder))
                return;

            string parent = Path.GetDirectoryName(GeneratedFolder).Replace('\\', '/');
            string leaf = Path.GetFileName(GeneratedFolder);

            if (!AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(parent));

            AssetDatabase.CreateFolder(parent, leaf);
        }

        static string SanitizeName(string source)
        {
            var buffer = new StringBuilder(source.Length);

            foreach (char c in source)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    buffer.Append(c);
                    continue;
                }

                buffer.Append('_');
            }

            return buffer.ToString();
        }
    }
}
