using UnityEditor;
using UnityEngine;

namespace Rush.EditorTools
{
    /// <summary>
    /// 유닛 프리팹의 더미 큐브 비주얼을 리깅된 FBX 모델로 교체한다.
    ///
    /// 더미는 지우지 않고 렌더러만 끈다. Monster.FindLookRenderer가 "켜져 있는 첫 렌더러"를
    /// 죽음 파편 색으로 쓰기 때문에, 끄기만 하면 자동으로 아트 쪽을 집는다.
    ///
    /// 멱등이다. 이미 Art 자식이 있으면 건드리지 않으므로 여러 번 실행해도 안전하다.
    /// EnsureUnitPrefab은 기존 프리팹이 있으면 그대로 두므로 전체 셋업을 다시 돌려도 유지된다.
    /// </summary>
    public static class RushUnitArtSetup
    {
        const string PrefabDir = "Assets/RushGame/Prefabs";
        const string CharacterDir = "Assets/fbx/character";
        const string ArtChildName = "Art";

        readonly struct Mapping
        {
            public readonly string Prefab;
            public readonly string Rig;

            public Mapping(string prefab, string rig)
            {
                Prefab = prefab;
                Rig = rig;
            }
        }

        /// <summary>
        /// 프리팹 - 리깅 모델 대응. Rider와 중간 보스 3종은 전용 아트가 아직 없어 뺐다
        /// (넣으려면 여기 줄만 추가하면 된다).
        /// </summary>
        static readonly Mapping[] Mappings =
        {
            new Mapping("Monster_Militia", $"{CharacterDir}/Anemy/AnemySoldier/AnemySoldier_rig.fbx"),
            new Mapping("Monster_HeavyInfantry", $"{CharacterDir}/Anemy/AnemyHeavy/AnemyHeavy_rig.fbx"),
            new Mapping("Monster_Scout", $"{CharacterDir}/Anemy/AnemyScout/AnemyScout_rig.fbx"),
            new Mapping("Monster_EnemyMage", $"{CharacterDir}/Anemy/AnemyMagician/AnemyMagician_rig.fbx"),
            new Mapping("Monster_Centurion", $"{CharacterDir}/Anemy/AnemyCaptain/AnemyCaptain_rig.fbx"),
            new Mapping("Soldier", $"{CharacterDir}/Hero/soldier/soldier_rig.fbx"),
        };

        [MenuItem("Rush/유닛 프리팹에 리깅 모델 연결", false, 300)]
        public static void Run()
        {
            int applied = 0;
            int skipped = 0;

            foreach (var mapping in Mappings)
            {
                if (Apply(mapping))
                    applied++;
                else
                    skipped++;
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[UnitArt] 연결 {applied}건 / 건너뜀 {skipped}건. " +
                      "모델 크기와 위치는 프리팹마다 확인이 필요하다 (FBX Scale Factor에 따라 달라진다).");
        }

        [MenuItem("Rush/유닛 프리팹 리깅 모델 해제", false, 301)]
        public static void Revert()
        {
            int reverted = 0;

            foreach (var mapping in Mappings)
            {
                if (RevertOne(mapping))
                    reverted++;
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[UnitArt] {reverted}건 되돌림 (더미 큐브 다시 켬).");
        }

        static bool Apply(Mapping mapping)
        {
            string path = $"{PrefabDir}/{mapping.Prefab}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"[UnitArt] 프리팹 없음: {path}");
                return false;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(mapping.Rig);

            if (model == null)
            {
                Debug.LogWarning($"[UnitArt] 리깅 모델 없음: {mapping.Rig}");
                return false;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);

            try
            {
                if (contents.transform.Find(ArtChildName) != null)
                {
                    Debug.Log($"[UnitArt] {mapping.Prefab}: 이미 연결됨 - 건너뜀");
                    return false;
                }

                SetDummyVisible(contents, false);

                var art = (GameObject)PrefabUtility.InstantiatePrefab(model, contents.transform);

                art.name = ArtChildName;
                art.transform.localPosition = Vector3.zero;
                art.transform.localRotation = Quaternion.identity;
                art.transform.localScale = Vector3.one;

                PrefabUtility.SaveAsPrefabAsset(contents, path);

                Debug.Log($"[UnitArt] {mapping.Prefab} <- {System.IO.Path.GetFileName(mapping.Rig)}");

                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static bool RevertOne(Mapping mapping)
        {
            string path = $"{PrefabDir}/{mapping.Prefab}.prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);

            try
            {
                var art = contents.transform.Find(ArtChildName);

                if (art == null)
                    return false;

                Object.DestroyImmediate(art.gameObject);
                SetDummyVisible(contents, true);

                PrefabUtility.SaveAsPrefabAsset(contents, path);

                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>더미 큐브(Art 바깥의 렌더러)를 켜고 끈다. 오브젝트는 남겨둔다.</summary>
        static void SetDummyVisible(GameObject root, bool visible)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (IsUnderArt(renderer.transform))
                    continue;

                renderer.enabled = visible;
            }
        }

        static bool IsUnderArt(Transform target)
        {
            for (var current = target; current != null; current = current.parent)
            {
                if (current.name == ArtChildName)
                    return true;
            }

            return false;
        }
    }
}
