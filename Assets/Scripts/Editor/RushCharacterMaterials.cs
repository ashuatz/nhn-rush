using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Rush.EditorTools
{
    /// <summary>
    /// 캐릭터 FBX에 diffuse 머티리얼을 연결한다 (환경 오브젝트와 같은 방식:
    /// 텍스처 1장당 머티리얼 1개 + FBX 임포터의 externalObjects 리매핑).
    ///
    /// 실제로 비어 있던 건 _rig.fbx 쪽이다. 기본 .fbx는 임포트 때 자동으로 잡혔지만
    /// 리깅 버전은 리매핑이 안 걸려 기본 머티리얼로 뜬다. 프리팹에 들어가는 건 리깅 쪽이다.
    ///
    /// 셰이더도 Simple Lit으로 맞춘다. URP Lit인 채로 두면 이 프로젝트의 LUT 높이 포그
    /// (SimpleLit 포워드 패스에 들어있다)를 못 받아 캐릭터만 안개에서 떠 보인다.
    ///
    /// 멱등이다. 이미 맞게 걸려 있으면 건너뛴다.
    /// </summary>
    public static class RushCharacterMaterials
    {
        const string CharacterRoot = "Assets/fbx/character";
        const string ShaderPath = "Assets/Shaders/SimpleLit/SimpleLit.shader";
        const string ShaderName = "Simple Lit";

        /// <summary>애니메이션 전용 FBX. 메시가 없어 대상이 아니다.</summary>
        const string AnimToken = "/Anim/";

        static readonly Regex DiffuseSuffix =
            new Regex(@"^(?<base>.+?)[ _-]?(diffuse|albedo|basecolor)$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [MenuItem("Rush/캐릭터 FBX 머티리얼 연결", false, 310)]
        public static void Run()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath) ?? Shader.Find(ShaderName);

            if (shader == null)
            {
                Debug.LogError($"[CharMat] 셰이더를 찾지 못했다: {ShaderPath}");
                return;
            }

            var fbxPaths = AssetDatabase.FindAssets("t:Model", new[] { CharacterRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .Where(p => !p.Replace('\\', '/').Contains(AnimToken))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int linked = 0;
            int fixedShader = 0;
            var warnings = new List<string>();

            foreach (var fbxPath in fbxPaths)
            {
                var texPath = FindDiffuse(fbxPath);

                if (string.IsNullOrEmpty(texPath))
                {
                    warnings.Add($"{Short(fbxPath)}: diffuse 텍스처를 못 찾음");
                    continue;
                }

                var material = FindOrCreateMaterial(texPath, shader, ref fixedShader);

                if (material == null)
                    continue;

                if (Remap(fbxPath, material))
                    linked++;
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[CharMat] 리매핑 {linked}건 / 셰이더 교체 {fixedShader}건 / 경고 {warnings.Count}건");

            foreach (var warning in warnings)
                Debug.LogWarning($"[CharMat] {warning}");
        }

        /// <summary>
        /// 텍스처를 이미 물고 있는 머티리얼이 같은 폴더에 있으면 그걸 쓴다.
        /// 자동 생성된 이름(m_soldier_Material_1 등)이 섞여 있어 이름이 아니라 참조로 찾는다.
        /// </summary>
        static Material FindOrCreateMaterial(string texPath, Shader shader, ref int fixedShader)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            var folder = Path.GetDirectoryName(texPath).Replace('\\', '/');

            var existing = AssetDatabase.FindAssets("t:Material", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == folder)
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .FirstOrDefault(m => m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") == texture);

            if (existing == null)
            {
                string baseName = StripDiffuse(Path.GetFileNameWithoutExtension(texPath));
                string matPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/m_{baseName}.mat");

                existing = new Material(shader);
                existing.SetTexture("_BaseMap", texture);

                AssetDatabase.CreateAsset(existing, matPath);
                Debug.Log($"[CharMat] 머티리얼 신규: {Short(matPath)}");
            }

            if (existing.shader != shader)
            {
                existing.shader = shader;

                // 셰이더를 바꾸면 _BaseMap이 떨어질 수 있어 다시 넣는다
                if (existing.HasProperty("_BaseMap"))
                    existing.SetTexture("_BaseMap", texture);

                EditorUtility.SetDirty(existing);
                fixedShader++;
            }

            return existing;
        }

        /// <summary>FBX의 모든 머티리얼 슬롯을 대상 머티리얼로 리매핑한다. 바뀐 게 있으면 true.</summary>
        static bool Remap(string fbxPath, Material material)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;

            if (importer == null)
                return false;

            var names = SlotNames(importer);

            if (names.Count == 0)
            {
                Debug.LogWarning($"[CharMat] {Short(fbxPath)}: 머티리얼 슬롯이 없음");
                return false;
            }

            var map = importer.GetExternalObjectMap();
            var pending = names.Where(n =>
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                return !map.TryGetValue(id, out var current) || current != material;
            }).ToList();

            if (pending.Count == 0)
                return false;

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;

            foreach (var name in pending)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);

            AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);

            Debug.Log($"[CharMat] {Short(fbxPath)} [{string.Join(", ", pending)}] -> {material.name}");

            return true;
        }

        /// <summary>FBX 안의 머티리얼 슬롯 이름. 임포터 직렬화 데이터에서 읽는다.</summary>
        static List<string> SlotNames(ModelImporter importer)
        {
            var result = new List<string>();
            var serialized = new SerializedObject(importer);
            var array = serialized.FindProperty("m_Materials");

            if (array == null || !array.isArray)
                return result;

            for (int i = 0; i < array.arraySize; i++)
            {
                string name = array.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;

                if (!string.IsNullOrEmpty(name) && !result.Contains(name))
                    result.Add(name);
            }

            return result;
        }

        /// <summary>
        /// FBX 폴더에서 시작해 위로 올라가며 이름이 맞는 diffuse를 찾는다.
        /// 이름 관계가 없으면 폴더에 텍스처가 하나뿐이어도 쓰지 않는다
        /// (magician 폴더의 wand_diffuse가 magician.fbx에 잘못 붙는 걸 막는다).
        /// </summary>
        static string FindDiffuse(string fbxPath)
        {
            string key = StripRig(Path.GetFileNameWithoutExtension(fbxPath));
            string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');

            while (!string.IsNullOrEmpty(dir) && dir.StartsWith(CharacterRoot, StringComparison.OrdinalIgnoreCase))
            {
                var best = AssetDatabase.FindAssets("t:Texture2D", new[] { dir })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == dir)
                    .Select(p => new { Path = p, Score = Score(key, StripDiffuse(Path.GetFileNameWithoutExtension(p))) })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();

                if (best != null)
                    return best.Path;

                string parent = Path.GetDirectoryName(dir)?.Replace('\\', '/');

                if (parent == dir)
                    break;

                dir = parent;
            }

            return null;
        }

        static int Score(string fbxName, string texBase)
        {
            if (string.IsNullOrEmpty(texBase))
                return 0;

            if (string.Equals(fbxName, texBase, StringComparison.OrdinalIgnoreCase))
                return 3;

            if (fbxName.StartsWith(texBase, StringComparison.OrdinalIgnoreCase))
                return 2;

            if (texBase.StartsWith(fbxName, StringComparison.OrdinalIgnoreCase))
                return 1;

            return 0;
        }

        static string StripRig(string name) =>
            name.EndsWith("_rig", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;

        static string StripDiffuse(string fileName)
        {
            var match = DiffuseSuffix.Match(fileName);

            return match.Success ? match.Groups["base"].Value : fileName;
        }

        static string Short(string path) =>
            path.StartsWith(CharacterRoot) ? path.Substring(CharacterRoot.Length + 1) : path;
    }
}
