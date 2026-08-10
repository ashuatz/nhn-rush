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

            /// <summary>목표 신장(m). 0이면 리그 fbx 임포트 스케일을 그대로 쓴다.</summary>
            public readonly float Height;

            public Mapping(string prefab, string rig, float height = 0f)
            {
                Prefab = prefab;
                Rig = rig;
                Height = height;
            }
        }

        /// <summary>
        /// 프리팹 - 리깅 모델 대응.
        /// 보스 4종은 전용 아트가 없어 성격이 가까운 모델을 크기만 키워 돌려 쓴다
        /// (신장은 RushSetupActions.MonsterModels와 같은 값).
        /// Rider는 watcherorb + FlyerHover 구성이라 검 모션이 맞지 않아 뺐다.
        /// </summary>
        static readonly Mapping[] Mappings =
        {
            new Mapping("Monster_Militia", $"{CharacterDir}/Anemy/AnemySoldier/AnemySoldier_Rig.fbx"),
            new Mapping("Monster_HeavyInfantry", $"{CharacterDir}/Anemy/AnemyHeavy/AnemyHeavy_Rig.fbx"),
            new Mapping("Monster_Scout", $"{CharacterDir}/Anemy/AnemyScout/AnemyScout_Rig.fbx"),
            new Mapping("Monster_EnemyMage", $"{CharacterDir}/Anemy/AnemyMagician/AnemyMagician_Rig.fbx"),
            new Mapping("Monster_Centurion", $"{CharacterDir}/Anemy/AnemyCaptain/AnemyCaptain_Rig.fbx"),
            new Mapping("Soldier", $"{CharacterDir}/Hero/soldier/soldier_rig.fbx"),
            new Mapping("Monster_MidBoss1", $"{CharacterDir}/Anemy/AnemyHeavy/AnemyHeavy_Rig.fbx", 1.5f),
            new Mapping("Monster_MidBoss2", $"{CharacterDir}/Anemy/AnemyCaptain/AnemyCaptain_Rig.fbx", 1.7f),
            new Mapping("Monster_MidBoss3", $"{CharacterDir}/Anemy/AnemyCaptain/AnemyCaptain_Rig.fbx", 1.9f),
            new Mapping("Monster_FinalBoss", $"{CharacterDir}/Anemy/AnemyCaptain/AnemyCaptain_Rig.fbx", 2.3f),
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
                // 이미 연결돼 있으면 모델은 그대로 두고 T포즈 정적 모델만 다시 끈다.
                // ApplyArtModels를 다시 돌리면 Visual/Model이 켜진 채 새로 생겨 아트가 겹쳐 보이는데,
                // 여기서 건너뛰기만 하면 그걸 되돌릴 방법이 없다.
                if (contents.transform.Find(ArtChildName) != null)
                {
                    // 켜진 게 없으면 저장도 하지 않는다. 매번 저장하면 바뀐 것 없이 프리팹 diff만 쌓인다.
                    if (SetDummyVisible(contents, false))
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        Debug.Log($"[UnitArt] {mapping.Prefab}: 이미 연결됨 - 겹친 정적 모델 정리");
                    }

                    return false;
                }

                SetDummyVisible(contents, false);

                var art = (GameObject)PrefabUtility.InstantiatePrefab(model, contents.transform);

                art.name = ArtChildName;
                art.transform.localPosition = Vector3.zero;
                art.transform.localRotation = Quaternion.identity;
                art.transform.localScale = Vector3.one;

                // 모델에 딸려 온 콜라이더는 슬롯 클릭 레이캐스트를 막으므로 지운다.
                // 루트의 전투용 콜라이더는 Art 밖에 있어 영향받지 않는다.
                RushSetupActions.StripColliders(art);

                // 보스처럼 신장이 지정된 것만 크기를 다시 잡는다.
                if (mapping.Height > 0f)
                    RushSetupActions.NormalizeToHeight(art.transform, mapping.Height, bottomY: 0f);

                PrefabUtility.SaveAsPrefabAsset(contents, path);

                string sizing = mapping.Height > 0f ? $", 신장 {mapping.Height:F2}m" : string.Empty;
                Debug.Log($"[UnitArt] {mapping.Prefab} <- {System.IO.Path.GetFileName(mapping.Rig)}{sizing}");

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

        /// <summary>
        /// 더미 큐브(Art 바깥의 렌더러)를 켜고 끈다. 오브젝트는 남겨둔다.
        /// 실제로 바뀐 렌더러가 있으면 true.
        /// </summary>
        static bool SetDummyVisible(GameObject root, bool visible)
        {
            bool changed = false;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (IsUnderArt(renderer.transform))
                    continue;

                if (renderer.enabled == visible)
                    continue;

                renderer.enabled = visible;
                changed = true;
            }

            return changed;
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
