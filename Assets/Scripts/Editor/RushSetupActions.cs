using System;
using System.Collections.Generic;
using System.IO;
using Rush.Combat;
using Rush.Data;
using Rush.Stage;
using Rush.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.EditorTools
{
    /// <summary>
    /// StageCommandWindow가 호출하는 셋업/검증 로직 모음.
    /// 모든 생성은 멱등: 이미 존재하는 에셋/오브젝트는 건너뛰고 참조만 채운다.
    /// 더미 비주얼은 전부 큐브 + 단색 머티리얼로 만들어 나중에 리소스만 갈아끼울 수 있게 한다.
    /// </summary>
    public static class RushSetupActions
    {
        const string Root = "Assets/RushGame";
        const string DataTowers = Root + "/Data/Towers";
        const string DataMonsters = Root + "/Data/Monsters";
        const string DataStages = Root + "/Data/Stages";
        const string DataDifficulty = Root + "/Data/Difficulty";
        const string DataRewards = Root + "/Data/Rewards";
        const string PrefabDir = Root + "/Prefabs";
        const string MaterialDir = Root + "/Materials";
        const string UiDir = Root + "/UI";
        const string SceneDir = Root + "/Scenes";
        const string ScenePath = SceneDir + "/Stage01.unity";

        /// <summary>기본 경로 웨이포인트. 씬 복구 시에도 이 정의를 기준으로 보충한다.</summary>
        static readonly Vector3[] DefaultWaypoints =
        {
            new Vector3(-11f, 0f, -5f),
            new Vector3(-4f, 0f, -5f),
            new Vector3(-4f, 0f, 4f),
            new Vector3(4f, 0f, 4f),
            new Vector3(4f, 0f, -4f),
            new Vector3(11f, 0f, -4f),
        };

        /// <summary>기본 타워 슬롯 위치. 슬롯 루트는 항상 스케일 1 (자식 비주얼만 납작하게).</summary>
        static readonly Vector3[] DefaultSlotPositions =
        {
            new Vector3(-8f, 0f, -3f),
            new Vector3(-6f, 0f, -7f),
            new Vector3(-2f, 0f, -2f),
            new Vector3(-6f, 0f, 2f),
            new Vector3(-1f, 0f, 6f),
            new Vector3(1.5f, 0f, 2f),
            new Vector3(6.5f, 0f, 0f),
            new Vector3(7f, 0f, -6.5f),
        };

        public static event Action<string> Reported;

        static void Report(string message)
        {
            Debug.Log($"[RushSetup] {message}");
            Reported?.Invoke(message);
        }

        // ---------- 1. 폴더 ----------

        public static void CreateFolders()
        {
            EnsureFolder(DataTowers);
            EnsureFolder(DataMonsters);
            EnsureFolder(DataStages);
            EnsureFolder(DataDifficulty);
            EnsureFolder(DataRewards);
            EnsureFolder(PrefabDir);
            EnsureFolder(MaterialDir);
            EnsureFolder(UiDir);
            EnsureFolder(SceneDir);

            Report("폴더 구조 확인 완료");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ---------- 2. 머티리얼 / 더미 프리팹 ----------

        static Material EnsureMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
                return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
                shader = Shader.Find("Standard");

            var mat = new Material(shader);

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);

            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);

            AssetDatabase.CreateAsset(mat, path);

            return mat;
        }

        public static void CreateDummyPrefabs()
        {
            CreateFolders();

            // 타워 4계열 (컴포넌트 포함, 비주얼은 큐브)
            EnsureTowerPrefab("Tower_Infantry", typeof(InfantryTower), EnsureMaterial("Mat_TowerInfantry", new Color(0.25f, 0.6f, 0.3f)));
            EnsureTowerPrefab("Tower_Archer", typeof(ArcherTower), EnsureMaterial("Mat_TowerArcher", new Color(0.25f, 0.45f, 0.8f)));
            EnsureTowerPrefab("Tower_Mage", typeof(MageTower), EnsureMaterial("Mat_TowerMage", new Color(0.6f, 0.3f, 0.8f)));
            EnsureTowerPrefab("Tower_Artillery", typeof(ArtilleryTower), EnsureMaterial("Mat_TowerArtillery", new Color(0.85f, 0.5f, 0.2f)));

            // 병사 / 발사체
            EnsureUnitPrefab("Soldier", typeof(Soldier), EnsureMaterial("Mat_Soldier", new Color(0.5f, 0.8f, 0.55f)), 0.45f, 0.28f);
            EnsureProjectilePrefab("Projectile_Arrow", EnsureMaterial("Mat_ProjArrow", new Color(0.85f, 0.85f, 0.8f)));
            EnsureProjectilePrefab("Projectile_Magic", EnsureMaterial("Mat_ProjMagic", new Color(0.75f, 0.4f, 1f)));
            EnsureProjectilePrefab("Projectile_Shell", EnsureMaterial("Mat_ProjShell", new Color(0.3f, 0.3f, 0.3f)));

            // 착탄 연출 (반투명 큐브가 한 박자 부풀었다 사라진다)
            EnsureImpactPrefab("Impact_Arrow", EnsureTranslucentMaterial("Mat_ImpactArrow", new Color(1f, 0.95f, 0.7f, 0.55f)));
            EnsureImpactPrefab("Impact_Magic", EnsureTranslucentMaterial("Mat_ImpactMagic", new Color(0.75f, 0.45f, 1f, 0.55f)));
            EnsureImpactPrefab("Impact_Shell", EnsureTranslucentMaterial("Mat_ImpactShell", new Color(1f, 0.6f, 0.25f, 0.5f)));

            // 추가 발사(실험 옵션)용 발사체
            EnsureProjectilePrefab("Projectile_Missile", EnsureMaterial("Mat_ProjMissile", new Color(1f, 0.5f, 0.15f)));
            EnsureProjectilePrefab("Projectile_Dagger", EnsureMaterial("Mat_ProjDagger", new Color(0.45f, 0.95f, 1f)));
            EnsureImpactPrefab("Impact_Missile", EnsureTranslucentMaterial("Mat_ImpactMissile", new Color(1f, 0.45f, 0.15f, 0.55f)));
            EnsureImpactPrefab("Impact_Dagger", EnsureTranslucentMaterial("Mat_ImpactDagger", new Color(0.45f, 0.95f, 1f, 0.5f)));

            // 궤적이 눈에 보이도록 발사체에 트레일을 붙인다 (곡선 연출의 핵심)
            EnsureProjectileTrail("Projectile_Arrow", new Color(1f, 0.97f, 0.8f), 0.18f, 0.1f);
            EnsureProjectileTrail("Projectile_Magic", new Color(0.8f, 0.5f, 1f), 0.4f, 0.22f);
            EnsureProjectileTrail("Projectile_Shell", new Color(1f, 0.65f, 0.3f), 0.35f, 0.16f);
            EnsureProjectileTrail("Projectile_Missile", new Color(1f, 0.55f, 0.2f), 0.5f, 0.18f);
            EnsureProjectileTrail("Projectile_Dagger", new Color(0.5f, 0.95f, 1f), 0.45f, 0.14f);

            // 몬스터 9종
            EnsureUnitPrefab("Monster_Infantry", typeof(Monster), EnsureMaterial("Mat_MonInfantry", new Color(0.8f, 0.3f, 0.3f)), 0.75f, 0.4f);
            EnsureUnitPrefab("Monster_Archer", typeof(Monster), EnsureMaterial("Mat_MonArcher", new Color(0.9f, 0.55f, 0.3f)), 0.65f, 0.35f);
            EnsureUnitPrefab("Monster_Tank", typeof(Monster), EnsureMaterial("Mat_MonTank", new Color(0.5f, 0.22f, 0.22f)), 1.05f, 0.55f);
            EnsureUnitPrefab("Monster_Fighter", typeof(Monster), EnsureMaterial("Mat_MonFighter", new Color(0.9f, 0.4f, 0.55f)), 0.7f, 0.35f);
            EnsureUnitPrefab("Monster_MagicInfantry", typeof(Monster), EnsureMaterial("Mat_MonMagicInfantry", new Color(0.55f, 0.35f, 0.9f)), 0.8f, 0.42f);
            EnsureUnitPrefab("Monster_MagicArcher", typeof(Monster), EnsureMaterial("Mat_MonMagicArcher", new Color(0.65f, 0.5f, 0.95f)), 0.68f, 0.36f);
            EnsureUnitPrefab("Monster_MagicTank", typeof(Monster), EnsureMaterial("Mat_MonMagicTank", new Color(0.4f, 0.2f, 0.6f)), 1.1f, 0.58f);
            EnsureUnitPrefab("Monster_MagicFighter", typeof(Monster), EnsureMaterial("Mat_MonMagicFighter", new Color(0.75f, 0.55f, 1f)), 0.72f, 0.38f);
            EnsureUnitPrefab("Monster_Boss", typeof(Monster), EnsureMaterial("Mat_MonBoss", new Color(0.25f, 0.05f, 0.05f)), 1.6f, 0.85f);

            AssetDatabase.SaveAssets();

            Report("더미 프리팹(큐브) 생성 완료");
        }

        static GameObject EnsureTowerPrefab(string name, Type towerType, Material mat)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
                return existing;

            var root = new GameObject(name);
            root.AddComponent(towerType);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";

            // 더미 유닛에 콜라이더가 있으면 슬롯 클릭 레이캐스트를 가로막는다
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());

            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            visual.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            return prefab;
        }

        static GameObject EnsureUnitPrefab(string name, Type componentType, Material mat, float size, float yCenter)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
                return existing;

            var root = new GameObject(name);
            root.AddComponent(componentType);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());

            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = new Vector3(0f, yCenter, 0f);
            visual.transform.localScale = new Vector3(size, size, size);
            visual.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            return prefab;
        }

        /// <summary>
        /// 발사체 프리팹에 트레일 자식이 없으면 추가한다. 이미 있으면 손대지 않는다.
        /// 착탄 시 Projectile이 이 자식을 떼어 내 잔상이 남게 한다.
        /// </summary>
        static void EnsureProjectileTrail(string prefabName, Color color, float time, float width)
        {
            string path = $"{PrefabDir}/{prefabName}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
                return;

            if (prefab.GetComponentInChildren<TrailRenderer>(true) != null)
                return;

            var contents = PrefabUtility.LoadPrefabContents(path);

            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(contents.transform);
            trailGo.transform.localPosition = Vector3.zero;

            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = time;
            trail.startWidth = width;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.03f;
            trail.numCapVertices = 2;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = EnsureTrailMaterial();

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        /// <summary>트레일용 머티리얼. 정점 색을 그대로 쓰는 셰이더가 필요하다.</summary>
        static Material EnsureTrailMaterial()
        {
            string path = $"{MaterialDir}/Mat_Trail.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
                return existing;

            var shader = Shader.Find("Sprites/Default");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            var mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);

            return mat;
        }

        static GameObject EnsureImpactPrefab(string name, Material mat)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
                return existing;

            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = name;
            UnityEngine.Object.DestroyImmediate(root.GetComponent<Collider>());

            root.transform.localScale = Vector3.one * 0.2f;
            root.GetComponent<MeshRenderer>().sharedMaterial = mat;
            root.AddComponent<ImpactBurst>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            return prefab;
        }

        static GameObject EnsureProjectilePrefab(string name, Material mat)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
                return existing;

            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = name;
            UnityEngine.Object.DestroyImmediate(root.GetComponent<Collider>());

            root.transform.localScale = Vector3.one * 0.25f;
            root.GetComponent<MeshRenderer>().sharedMaterial = mat;
            root.AddComponent<Projectile>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            return prefab;
        }

        // ---------- 2-1. 아트 모델 적용 ----------

        const string FbxEnvironment = "Assets/fbx/environment";
        const string FbxCharacter = "Assets/fbx/character";

        /// <summary>
        /// fbx 아트 모델을 프리팹에 배선한다. 머티리얼은 건드리지 않는다 (임포트 상태 그대로).
        /// 타워: TierVisuals/Tier1~3 자식으로 주입하고 더미 큐브는 끈다 (레벨별 온오프는 Tower가 처리).
        /// 병사: Visual 노드 아래에 모델을 넣고 큐브 렌더러만 끈다 (런지 모션 유지).
        /// 재실행하면 기존 주입분을 지우고 다시 만든다.
        /// </summary>
        public static void ApplyArtModels()
        {
            InjectTowerTiers("Tower_Archer", "archertower", 1.5f);
            InjectTowerTiers("Tower_Infantry", "barracktower", 1.5f);
            InjectTowerTiers("Tower_Mage", "magiciantower", 1.6f);
            InjectTowerTiers("Tower_Artillery", "cannontower", 1.4f);

            InjectSoldierModel();

            AssetDatabase.SaveAssets();

            Report("아트 모델 적용 완료 (머티리얼은 임포트 상태 유지)");
        }

        static void InjectTowerTiers(string prefabName, string fbxFamily, float targetHeight)
        {
            string prefabPath = $"{PrefabDir}/{prefabName}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Report($"{prefabName}: 프리팹이 없어 건너뜀 (더미 프리팹 먼저 생성)");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(prefabPath);

            var oldTiers = contents.transform.Find("TierVisuals");

            if (oldTiers != null)
                UnityEngine.Object.DestroyImmediate(oldTiers.gameObject);

            var tierRoot = new GameObject("TierVisuals");
            tierRoot.transform.SetParent(contents.transform);
            tierRoot.transform.localPosition = Vector3.zero;

            int injected = 0;

            for (int tier = 1; tier <= 3; tier++)
            {
                string fbxPath = $"{FbxEnvironment}/{fbxFamily}/{fbxFamily}0{tier}/{fbxFamily}0{tier}.fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

                if (model == null)
                {
                    Report($"{prefabName}: {fbxPath} 없음 - 티어 {tier} 건너뜀");
                    continue;
                }

                var wrapper = new GameObject($"Tier{tier}");
                wrapper.transform.SetParent(tierRoot.transform);
                wrapper.transform.localPosition = Vector3.zero;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, wrapper.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                NormalizeToHeight(instance.transform, targetHeight, bottomY: 0f);
                StripColliders(instance);

                // 기본은 1티어만 보이게. 런타임에는 Tower.OnLevelChanged가 다시 정한다.
                wrapper.SetActive(tier == 1);

                injected++;
            }

            // 더미 큐브는 티어 모델이 하나라도 있으면 꺼 둔다 (없으면 폴백으로 유지)
            var visual = contents.transform.Find("Visual");

            if (visual != null)
                visual.gameObject.SetActive(injected == 0);

            if (injected == 0)
                UnityEngine.Object.DestroyImmediate(tierRoot);

            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            Report($"{prefabName}: 티어 모델 {injected}개 주입");
        }

        static void InjectSoldierModel()
        {
            string prefabPath = $"{PrefabDir}/Soldier.prefab";
            string fbxPath = $"{FbxCharacter}/soldier.fbx";

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

            if (model == null)
            {
                Report($"Soldier: {fbxPath} 없음 - 건너뜀");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Report("Soldier: 프리팹이 없어 건너뜀");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            var visual = contents.transform.Find("Visual");

            if (visual == null)
            {
                PrefabUtility.UnloadPrefabContents(contents);
                Report("Soldier: Visual 노드가 없어 건너뜀");
                return;
            }

            var oldModel = visual.Find("Model");

            if (oldModel != null)
                UnityEngine.Object.DestroyImmediate(oldModel.gameObject);

            // 큐브 렌더러만 끈다. Visual 트랜스폼은 런지 모션이 계속 쓴다.
            var cubeRenderer = visual.GetComponent<MeshRenderer>();

            if (cubeRenderer != null)
                cubeRenderer.enabled = false;

            var wrapper = new GameObject("Model");
            wrapper.transform.SetParent(visual);
            wrapper.transform.localPosition = Vector3.zero;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, wrapper.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // 병사 루트는 지면(y=0)에 서고 Visual은 위로 떠 있으므로, 모델 바닥을 지면에 맞춘다
            NormalizeToHeight(instance.transform, 0.9f, bottomY: -visual.localPosition.y);
            StripColliders(instance);

            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            Report("Soldier: 캐릭터 모델 주입 (애니메이션은 아직 미셋업)");
        }

        /// <summary>
        /// 렌더러 바운드 기준으로 목표 높이에 맞게 균등 스케일하고,
        /// 바닥이 부모 로컬 bottomY, 수평 중심이 0이 되도록 위치를 보정한다.
        /// </summary>
        static void NormalizeToHeight(Transform instance, float targetHeight, float bottomY)
        {
            var bounds = CalculateRendererBounds(instance);

            if (bounds.size.y <= 0.0001f)
                return;

            float scale = targetHeight / bounds.size.y;
            instance.localScale = instance.localScale * scale;

            // 스케일 반영 후 바운드를 다시 재서 바닥/중심을 맞춘다
            bounds = CalculateRendererBounds(instance);

            Vector3 offset = new Vector3(-bounds.center.x, bottomY - bounds.min.y, -bounds.center.z);
            instance.localPosition += offset;
        }

        static Bounds CalculateRendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
                return new Bounds(root.position, Vector3.zero);

            var bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        /// <summary>모델에 딸려 온 콜라이더는 슬롯 클릭 레이캐스트를 방해하므로 제거한다.</summary>
        static void StripColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
        }

        /// <summary>씬의 타워 슬롯 비주얼을 towerbase 모델로 교체한다 (있을 때만).</summary>
        public static void UpgradeSlotVisuals()
        {
            string fbxPath = $"{FbxEnvironment}/towerbase/towerbase/towerbase.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

            if (model == null)
            {
                Report("towerbase 모델 없음 - 슬롯 비주얼 유지");
                return;
            }

            var slots = UnityEngine.Object.FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);
            int upgraded = 0;

            foreach (var slot in slots)
            {
                var visual = slot.transform.Find("Visual");

                // 이미 모델 기반이면 건너뛴다 (큐브 프리미티브만 교체)
                if (visual != null && visual.GetComponent<MeshFilter>() == null)
                    continue;

                if (visual != null)
                    UnityEngine.Object.DestroyImmediate(visual.gameObject);

                var wrapper = new GameObject("Visual");
                wrapper.transform.SetParent(slot.transform);
                wrapper.transform.localPosition = Vector3.zero;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, wrapper.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                NormalizeSlotBase(instance.transform);
                StripColliders(instance);

                upgraded++;
            }

            if (upgraded > 0)
                Report($"슬롯 비주얼 {upgraded}개를 towerbase 모델로 교체");
        }

        /// <summary>슬롯 받침은 높이가 아니라 발자국(가로세로 1.1)에 맞춰 정규화한다.</summary>
        static void NormalizeSlotBase(Transform instance)
        {
            var bounds = CalculateRendererBounds(instance);
            float footprint = Mathf.Max(bounds.size.x, bounds.size.z);

            if (footprint <= 0.0001f)
                return;

            float scale = 1.1f / footprint;
            instance.localScale = instance.localScale * scale;

            bounds = CalculateRendererBounds(instance);

            Vector3 offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            instance.localPosition += offset;
        }

        // ---------- 3. 타워 데이터 ----------

        public static void CreateTowerData()
        {
            CreateDummyPrefabs();

            var infantry = EnsureAsset<TowerData>($"{DataTowers}/Tower_Infantry.asset", data =>
            {
                data.Type = TowerType.Infantry;
                data.DamageType = DamageType.Physical;
                data.Levels = new[]
                {
                    new TowerLevelStat { DisplayName = "민병대 초소", Cost = 70, Range = 2.5f, AttackInterval = 1f, SoldierCount = 2, SoldierHp = 60, SoldierDamage = 6, SoldierAttackInterval = 1f, SoldierRespawnSeconds = 8f },
                    new TowerLevelStat { DisplayName = "정예 보병대", Cost = 110, Range = 2.5f, AttackInterval = 1f, SoldierCount = 2, SoldierHp = 90, SoldierDamage = 9, SoldierAttackInterval = 1f, SoldierRespawnSeconds = 8f },
                    new TowerLevelStat { DisplayName = "중장 보병대", Cost = 160, Range = 2.8f, AttackInterval = 1f, SoldierCount = 3, SoldierHp = 120, SoldierDamage = 12, SoldierAttackInterval = 1f, SoldierRespawnSeconds = 8f },
                    new TowerLevelStat { DisplayName = "왕실 근위대", Cost = 240, Range = 3f, AttackInterval = 1f, SoldierCount = 3, SoldierHp = 160, SoldierDamage = 16, SoldierAttackInterval = 1f, SoldierRespawnSeconds = 8f },
                };
            });

            var archer = EnsureAsset<TowerData>($"{DataTowers}/Tower_Archer.asset", data =>
            {
                data.Type = TowerType.Archer;
                data.DamageType = DamageType.Physical;
                data.ProjectileSpeed = 14f;
                data.Levels = new[]
                {
                    new TowerLevelStat { DisplayName = "궁수 초소", Cost = 70, Damage = 8, Range = 5f, AttackInterval = 0.9f },
                    new TowerLevelStat { DisplayName = "장궁병 부대", Cost = 110, Damage = 12, Range = 5.5f, AttackInterval = 0.9f },
                    new TowerLevelStat { DisplayName = "정예 궁병대", Cost = 160, Damage = 16, Range = 5.5f, AttackInterval = 0.7f },
                    new TowerLevelStat { DisplayName = "왕실 궁병단", Cost = 240, Damage = 22, Range = 6f, AttackInterval = 0.6f },
                };
            });

            var mage = EnsureAsset<TowerData>($"{DataTowers}/Tower_Mage.asset", data =>
            {
                data.Type = TowerType.Mage;
                data.DamageType = DamageType.Magical;
                data.ProjectileSpeed = 10f;
                data.Levels = new[]
                {
                    new TowerLevelStat { DisplayName = "견습 마법사 탑", Cost = 100, Damage = 12, Range = 4.5f, AttackInterval = 1.4f, SlowPercent = 0.25f, SlowDuration = 2f },
                    new TowerLevelStat { DisplayName = "상급 마법사 탑", Cost = 160, Damage = 18, Range = 4.5f, AttackInterval = 1.4f, SlowPercent = 0.25f, SlowDuration = 2.5f },
                    new TowerLevelStat { DisplayName = "대마법사 탑", Cost = 240, Damage = 26, Range = 5f, AttackInterval = 1.4f, SlowPercent = 0.3f, SlowDuration = 2.5f },
                    new TowerLevelStat { DisplayName = "대현자 탑", Cost = 360, Damage = 36, Range = 5f, AttackInterval = 1.4f, SlowPercent = 0.35f, SlowDuration = 3f },
                };
            });

            var artillery = EnsureAsset<TowerData>($"{DataTowers}/Tower_Artillery.asset", data =>
            {
                data.Type = TowerType.Artillery;
                data.DamageType = DamageType.Physical;
                data.ProjectileSpeed = 8f;
                data.Levels = new[]
                {
                    new TowerLevelStat { DisplayName = "화포 진지", Cost = 125, Damage = 18, Range = 4.5f, AttackInterval = 2.5f, SplashRadius = 1.2f, ArmorPierce = 0.5f },
                    new TowerLevelStat { DisplayName = "중포 진지", Cost = 220, Damage = 28, Range = 4.5f, AttackInterval = 2.5f, SplashRadius = 1.4f, ArmorPierce = 0.5f },
                    new TowerLevelStat { DisplayName = "공성포 진지", Cost = 320, Damage = 40, Range = 5f, AttackInterval = 2.5f, SplashRadius = 1.6f, ArmorPierce = 0.5f },
                    new TowerLevelStat { DisplayName = "왕실 공성단", Cost = 480, Damage = 56, Range = 5f, AttackInterval = 2.5f, SplashRadius = 1.8f, ArmorPierce = 0.5f },
                };
            });

            // 프리팹 참조는 비어 있을 때만 채운다 (수동 교체 존중)
            WireTowerPrefabs(infantry, "Tower_Infantry", null, "Soldier", null);
            WireTowerPrefabs(archer, "Tower_Archer", "Projectile_Arrow", null, "Impact_Arrow");
            WireTowerPrefabs(mage, "Tower_Mage", "Projectile_Magic", null, "Impact_Magic");
            WireTowerPrefabs(artillery, "Tower_Artillery", "Projectile_Shell", null, "Impact_Shell");

            ApplyMotionPresets(force: false);

            AssetDatabase.SaveAssets();

            Report("타워 데이터 4종 생성 완료");
        }

        static void WireTowerPrefabs(TowerData data, string towerPrefab, string projectilePrefab,
            string soldierPrefab, string impactPrefab)
        {
            bool dirty = false;

            if (data.TowerPrefab == null)
            {
                data.TowerPrefab = LoadPrefab(towerPrefab);
                dirty = true;
            }

            if (projectilePrefab != null && data.ProjectilePrefab == null)
            {
                data.ProjectilePrefab = LoadPrefab(projectilePrefab);
                dirty = true;
            }

            if (soldierPrefab != null && data.SoldierPrefab == null)
            {
                data.SoldierPrefab = LoadPrefab(soldierPrefab);
                dirty = true;
            }

            if (impactPrefab != null && data.ImpactPrefab == null)
            {
                data.ImpactPrefab = LoadPrefab(impactPrefab);
                dirty = true;
            }

            if (dirty)
                EditorUtility.SetDirty(data);
        }

        // ---------- 3-1. 공격 연출 프리셋 ----------

        /// <summary>
        /// 계열마다 다른 발사 연출을 채운다.
        /// force가 false면 아직 설정되지 않은(ShotCount 0) 데이터만 채워 수동 조정을 존중한다.
        /// </summary>
        public static void ApplyMotionPresets(bool force)
        {
            int applied = 0;

            applied += ApplyMotion("Tower_Archer", force, motion =>
            {
                // 궁병: 얕게 휘는 화살 2연사. 빠르고 가볍게 읽히도록.
                motion.Kind = MotionKind.Arc;
                motion.ShotCount = 2;
                motion.ShotInterval = 0.07f;
                motion.EndScatter = 0.18f;
                motion.SpinPerSecond = 0f;
                motion.SampleMin = 0.3f;
                motion.SampleMax = 0.5f;
                motion.BulgeFactor = 1.6f;
                motion.BulgeMin = 0.15f;
                motion.BulgeMax = 0.6f;
                motion.BulgeWorldUp = false;
                motion.RollSteps = 2;
                motion.WanderAmplitude = 0f;
                motion.TimeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            });

            applied += ApplyMotion("Tower_Mage", force, motion =>
            {
                // 마도: 사방으로 휘며 흔들리는 마법탄. 후반부에 가속한다.
                motion.Kind = MotionKind.Wander;
                motion.ShotCount = 1;
                motion.ShotInterval = 0.1f;
                motion.EndScatter = 0.1f;
                motion.SpinPerSecond = 0f;
                motion.SampleMin = 0.15f;
                motion.SampleMax = 0.45f;
                motion.BulgeFactor = 2.2f;
                motion.BulgeMin = 0.3f;
                motion.BulgeMax = 1.1f;
                motion.BulgeWorldUp = false;
                motion.RollSteps = 12;
                motion.WanderAmplitude = 0.45f;
                motion.WanderTurn = 0.75f;
                motion.TimeCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
            });

            applied += ApplyMotion("Tower_Artillery", force, motion =>
            {
                // 포병: 높이 떠서 떨어지는 곡사. 회전하는 포탄 + 넓게 흩어지는 착탄.
                motion.Kind = MotionKind.Lob;
                motion.ShotCount = 1;
                motion.ShotInterval = 0.12f;
                motion.EndScatter = 0.35f;
                motion.SpinPerSecond = 540f;
                motion.SampleMin = 0.45f;
                motion.SampleMax = 0.55f;
                motion.BulgeFactor = 9f;
                motion.BulgeMin = 1.8f;
                motion.BulgeMax = 3.5f;
                motion.BulgeWorldUp = true;
                motion.RollSteps = 0;
                motion.WanderAmplitude = 0f;
                motion.TimeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            });

            foreach (var name in new[] { "Tower_Archer", "Tower_Mage", "Tower_Artillery" })
            {
                var data = AssetDatabase.LoadAssetAtPath<TowerData>($"{DataTowers}/{name}.asset");

                if (data != null)
                    ConfigureExtras(data, force);
            }

            AssetDatabase.SaveAssets();

            Report($"공격 연출 프리셋 적용: {applied}건");
        }

        /// <summary>추가 발사(확률 발사 / 처치 시 발사)의 기본값을 채운다. 기본은 켜짐.</summary>
        static void ConfigureExtras(TowerData data, bool force)
        {
            if (data.Extras == null)
                data.Extras = new AttackExtras();

            var extras = data.Extras;
            bool dirty = false;

            // 참조는 비어 있으면 언제나 보충한다 (일부만 지워진 상태도 셋업 재실행으로 복구)
            dirty |= FillPrefabSlot(ref extras.ProcPrefab, "Projectile_Missile");
            dirty |= FillPrefabSlot(ref extras.ProcImpactPrefab, "Impact_Missile");
            dirty |= FillPrefabSlot(ref extras.OnKillPrefab, "Projectile_Dagger");
            dirty |= FillPrefabSlot(ref extras.OnKillImpactPrefab, "Impact_Dagger");

            if (extras.ProcMotion == null)
                extras.ProcMotion = new ProjectileMotion();

            if (extras.OnKillMotion == null)
                extras.OnKillMotion = new ProjectileMotion();

            // 두 궤적 프리셋은 각각 독립적으로 판정한다. 프리셋을 새로 넣을 때는 켜진 상태로 둔다.
            if (force || !extras.ProcMotion.IsConfigured)
            {
                ApplyMissileMotion(extras.ProcMotion);
                extras.ProcEnabled = true;
                dirty = true;
            }

            if (force || !extras.OnKillMotion.IsConfigured)
            {
                ApplyDaggerMotion(extras.OnKillMotion);
                extras.OnKillEnabled = true;
                dirty = true;
            }

            if (dirty)
                EditorUtility.SetDirty(data);
        }

        static bool FillPrefabSlot(ref GameObject slot, string prefabName)
        {
            if (slot != null)
                return false;

            slot = LoadPrefab(prefabName);

            return slot != null;
        }

        static void ApplyMissileMotion(ProjectileMotion proc)
        {
            // 미사일: 높이 솟았다가 떨어지는 곡사
            proc.Kind = MotionKind.Lob;
            proc.ShotCount = 1;
            proc.ShotInterval = 0.08f;
            proc.EndScatter = 0.25f;
            proc.SpinPerSecond = 0f;
            proc.SampleMin = 0.4f;
            proc.SampleMax = 0.6f;
            proc.BulgeFactor = 12f;
            proc.BulgeMin = 2f;
            proc.BulgeMax = 4.5f;
            proc.BulgeWorldUp = true;
            proc.RollSteps = 0;
            proc.WanderAmplitude = 0f;
            proc.TimeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        static void ApplyDaggerMotion(ProjectileMotion onKill)
        {
            // 단검: 사방으로 크게 휘며 쫓아가는 궤적
            onKill.Kind = MotionKind.Wander;
            onKill.ShotCount = 1;
            onKill.ShotInterval = 0.05f;
            onKill.EndScatter = 0.12f;
            onKill.SpinPerSecond = 720f;
            onKill.SampleMin = 0.1f;
            onKill.SampleMax = 0.5f;
            onKill.BulgeFactor = 3.5f;
            onKill.BulgeMin = 0.5f;
            onKill.BulgeMax = 1.8f;
            onKill.BulgeWorldUp = false;
            onKill.RollSteps = 12;
            onKill.WanderAmplitude = 0.35f;
            onKill.WanderTurn = 0.8f;
            onKill.TimeCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
        }

        static int ApplyMotion(string assetName, bool force, Action<ProjectileMotion> setup)
        {
            var data = AssetDatabase.LoadAssetAtPath<TowerData>($"{DataTowers}/{assetName}.asset");

            if (data == null)
                return 0;

            if (data.Motion == null)
                data.Motion = new ProjectileMotion();

            if (!force && data.Motion.IsConfigured)
                return 0;

            setup(data.Motion);
            EditorUtility.SetDirty(data);

            return 1;
        }

        static GameObject LoadPrefab(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");
        }

        // ---------- 4. 몬스터 데이터 ----------

        public static void CreateMonsterData()
        {
            CreateDummyPrefabs();

            EnsureMonster("Monster_Infantry", "보병", data =>
            {
                data.MaxHp = 60; data.MoveSpeed = 1.6f;
                data.PhysicalDefense = DefenseGrade.Low; data.MagicalDefense = DefenseGrade.Low;
                data.GoldReward = 5; data.MeleeDamage = 5;
            });

            EnsureMonster("Monster_Archer", "궁병", data =>
            {
                data.MaxHp = 45; data.MoveSpeed = 1.6f;
                data.PhysicalDefense = DefenseGrade.Low; data.MagicalDefense = DefenseGrade.Low;
                data.GoldReward = 6; data.MeleeDamage = 3;
                data.RangedDamage = 6; data.RangedRange = 4f; data.RangedInterval = 1.5f;
            });

            EnsureMonster("Monster_Tank", "탱크", data =>
            {
                data.MaxHp = 220; data.MoveSpeed = 0.9f;
                data.PhysicalDefense = DefenseGrade.High; data.MagicalDefense = DefenseGrade.Medium;
                data.GoldReward = 12; data.MeleeDamage = 12; data.MeleeInterval = 1.2f;
            });

            EnsureMonster("Monster_Fighter", "전투기", data =>
            {
                data.MaxHp = 80; data.MoveSpeed = 2.2f; data.IsFlying = true;
                data.PhysicalDefense = DefenseGrade.Medium; data.MagicalDefense = DefenseGrade.Medium;
                data.GoldReward = 8;
            });

            EnsureMonster("Monster_MagicInfantry", "마법 보병", data =>
            {
                data.MaxHp = 110; data.MoveSpeed = 1.6f;
                data.PhysicalDefense = DefenseGrade.Medium; data.MagicalDefense = DefenseGrade.Medium;
                data.GoldReward = 9; data.MeleeDamage = 7; data.RegenPerSecond = 2f;
            });

            EnsureMonster("Monster_MagicArcher", "마법 궁병", data =>
            {
                data.MaxHp = 70; data.MoveSpeed = 1.6f;
                data.PhysicalDefense = DefenseGrade.Medium; data.MagicalDefense = DefenseGrade.Low;
                data.GoldReward = 10; data.MeleeDamage = 4; data.RegenPerSecond = 1f;
                data.RangedDamage = 9; data.RangedRange = 5f; data.RangedInterval = 1.5f;
            });

            EnsureMonster("Monster_MagicTank", "마법 탱크", data =>
            {
                data.MaxHp = 320; data.MoveSpeed = 1f;
                data.PhysicalDefense = DefenseGrade.Great; data.MagicalDefense = DefenseGrade.Medium;
                data.GoldReward = 18; data.MeleeDamage = 16; data.MeleeInterval = 1.2f; data.RegenPerSecond = 3f;
            });

            EnsureMonster("Monster_MagicFighter", "마법 전투기", data =>
            {
                data.MaxHp = 140; data.MoveSpeed = 2.4f; data.IsFlying = true;
                data.PhysicalDefense = DefenseGrade.High; data.MagicalDefense = DefenseGrade.High;
                data.GoldReward = 15; data.RegenPerSecond = 2f;
            });

            EnsureMonster("Monster_Boss", "챕터 보스", data =>
            {
                data.MaxHp = 1500; data.MoveSpeed = 0.7f;
                data.PhysicalDefense = DefenseGrade.Great; data.MagicalDefense = DefenseGrade.Great;
                data.GoldReward = 100; data.LifeDamage = 5; data.MeleeDamage = 25; data.MeleeInterval = 1.5f;
            });

            AssetDatabase.SaveAssets();

            Report("몬스터 데이터 9종 생성 완료");
        }

        static void EnsureMonster(string assetName, string displayName, Action<MonsterData> setup)
        {
            var data = EnsureAsset<MonsterData>($"{DataMonsters}/{assetName}.asset", d =>
            {
                d.DisplayName = displayName;
                setup(d);
            });

            if (data.Prefab != null)
                return;

            data.Prefab = LoadPrefab(assetName);
            EditorUtility.SetDirty(data);
        }

        // ---------- 5. 스테이지 / 난이도 데이터 ----------

        public static void CreateStageAndDifficultyData()
        {
            CreateMonsterData();

            EnsureAsset<DifficultyPreset>($"{DataDifficulty}/Difficulty_Casual.asset", d =>
            {
                d.DisplayName = "캐주얼";
                d.EnemyHpMultiplier = 0.8f;
                d.SoldierHpMultiplier = 1.2f;
            });

            EnsureAsset<DifficultyPreset>($"{DataDifficulty}/Difficulty_Normal.asset", d =>
            {
                d.DisplayName = "노멀";
                d.EnemyHpMultiplier = 1f;
                d.SoldierHpMultiplier = 1f;
            });

            EnsureAsset<DifficultyPreset>($"{DataDifficulty}/Difficulty_Veteran.asset", d =>
            {
                d.DisplayName = "베테랑";
                d.EnemyHpMultiplier = 1.2f;
                d.SoldierHpMultiplier = 0.8f;
            });

            EnsureAsset<StageData>($"{DataStages}/Stage01.asset", BuildStage01);

            AssetDatabase.SaveAssets();

            Report("스테이지/난이도 데이터 생성 완료");
        }

        static void BuildStage01(StageData stage)
        {
            var infantry = LoadMonster("Monster_Infantry");
            var archer = LoadMonster("Monster_Archer");
            var tank = LoadMonster("Monster_Tank");
            var fighter = LoadMonster("Monster_Fighter");
            var mInfantry = LoadMonster("Monster_MagicInfantry");
            var mArcher = LoadMonster("Monster_MagicArcher");
            var mTank = LoadMonster("Monster_MagicTank");
            var mFighter = LoadMonster("Monster_MagicFighter");
            var boss = LoadMonster("Monster_Boss");

            stage.StartLife = 20;
            stage.StartGold = 260;
            stage.FirstWaveDelay = 15f;
            stage.WaveInterval = 30f;
            stage.EarlyCallGoldPerSecond = 2f;

            stage.Waves = new[]
            {
                Wave(Entry(infantry, 6, 1.2f)),
                Wave(Entry(infantry, 9, 1f)),
                Wave(Entry(infantry, 6, 1.2f), Entry(archer, 4, 1.5f, 3f)),
                Wave(Entry(infantry, 8, 1f), Entry(archer, 6, 1.2f, 2f)),
                Wave(Entry(tank, 3, 3f), Entry(fighter, 4, 1.5f, 4f)),
                Wave(Entry(tank, 4, 2.5f), Entry(fighter, 6, 1.2f, 3f), Entry(infantry, 6, 1f, 6f)),
                Wave(Entry(mInfantry, 8, 1.2f), Entry(mArcher, 4, 1.5f, 4f)),
                Wave(Entry(mInfantry, 10, 1f), Entry(mArcher, 6, 1.2f, 3f)),
                Wave(Entry(mTank, 3, 4f), Entry(mFighter, 5, 1.5f, 5f)),
                Wave(Entry(boss, 1, 1f)),
            };
        }

        static MonsterData LoadMonster(string name)
        {
            return AssetDatabase.LoadAssetAtPath<MonsterData>($"{DataMonsters}/{name}.asset");
        }

        static WaveData Wave(params SpawnEntry[] entries)
        {
            return new WaveData { Entries = entries };
        }

        static SpawnEntry Entry(MonsterData monster, int count, float interval, float startDelay = 0f)
        {
            return new SpawnEntry { Monster = monster, Count = count, Interval = interval, StartDelay = startDelay };
        }

        // ---------- 6. UI (PanelSettings) ----------

        public static PanelSettings CreatePanelSettings()
        {
            CreateFolders();

            string tssPath = $"{UiDir}/RushRuntimeTheme.tss";

            if (AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tssPath) == null)
            {
                File.WriteAllText(tssPath, "@import url(\"unity-theme://default\");");
                AssetDatabase.ImportAsset(tssPath);
            }

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tssPath);

            string psPath = $"{UiDir}/RushPanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath);

            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = theme;
                AssetDatabase.CreateAsset(ps, psPath);
            }

            if (ps.themeStyleSheet == null)
            {
                ps.themeStyleSheet = theme;
                EditorUtility.SetDirty(ps);
            }

            AssetDatabase.SaveAssets();

            Report("PanelSettings 준비 완료");

            return ps;
        }

        // ---------- 6-1. 보상 데이터 ----------

        /// <summary>보상 카드 54종과 플로우 설정을 생성/동기화한다. 수치는 기존 조정값을 존중한다.</summary>
        public static RewardFlowConfig CreateRewardAssets(bool forceValues = false)
        {
            CreateFolders();

            var cards = RushRewardCatalog.EnsureAll(DataRewards, forceValues);

            string configPath = $"{DataRewards}/RewardFlowConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<RewardFlowConfig>(configPath);

            if (config == null)
            {
                config = ScriptableObject.CreateInstance<RewardFlowConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }

            config.Cards = cards.ToArray();
            EditorUtility.SetDirty(config);

            AssetDatabase.SaveAssets();

            Report($"보상 데이터 동기화 완료 (카드 {cards.Count}종)");

            return config;
        }

        // ---------- 7. 전체 에셋 원클릭 ----------

        public static void CreateAllAssets()
        {
            CreateFolders();
            CreateDummyPrefabs();
            ApplyArtModels();
            CreateTowerData();
            CreateMonsterData();
            CreateStageAndDifficultyData();
            CreateRewardAssets();
            CreatePanelSettings();

            Report("전체 에셋 셋업 완료");
        }

        // ---------- 8. 씬 셋업 ----------

        public static void SetupScene()
        {
            // 씬을 교체하기 전에 현재 씬의 미저장 변경을 사용자에게 확인한다 (취소하면 셋업 중단)
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Report("씬 저장이 취소되어 셋업을 중단함");
                return;
            }

            CreateAllAssets();

            // 씬 열기 (없으면 새로 만들어 저장)
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(newScene, ScenePath);
            }
            else if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            SetupCamera();
            SetupGround();
            var path = SetupPath();
            BakePathVisual(path);
            SetupSlots();
            UpgradeSlotVisuals();
            var stage = SetupStageController(path);
            SetupGameUI(stage);

            AddSceneToBuildSettings();

            // 에디터가 포커스를 잃어도 플레이 루프가 계속 돌게 한다 (원격/자동화 작업 시 정지 방지)
            PlayerSettings.runInBackground = true;

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Report("씬 셋업 완료: " + ScenePath);
        }

        static void SetupCamera()
        {
            var cam = Camera.main;

            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            cam.transform.position = new Vector3(0f, 16f, -11f);
            cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        }

        static void SetupGround()
        {
            var ground = GameObject.Find("Ground");

            if (ground != null)
                return;

            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(26f, 0.2f, 18f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = EnsureMaterial("Mat_Ground", new Color(0.35f, 0.38f, 0.32f));
        }

        /// <summary>경로 루트와 웨이포인트를 보장한다. 루트만 있고 포인트가 모자라면 기본 정의로 보충한다.</summary>
        static PathRoute SetupPath()
        {
            var pathGo = GameObject.Find("Path");

            if (pathGo == null)
                pathGo = new GameObject("Path");

            for (int i = pathGo.transform.childCount; i < DefaultWaypoints.Length; i++)
            {
                var wp = new GameObject($"P{i}");
                wp.transform.SetParent(pathGo.transform);
                wp.transform.position = DefaultWaypoints[i];
            }

            var route = pathGo.GetComponent<PathRoute>();

            if (route == null)
                route = pathGo.AddComponent<PathRoute>();

            route.CachePoints();

            return route;
        }

        /// <summary>
        /// 경로를 바닥 타일로 베이크한다. 런타임 생성 없이 에디트 모드에서 바로 눈으로 확인할 수 있다.
        /// 다시 호출하면 기존 타일을 지우고 현재 웨이포인트 기준으로 재생성한다.
        /// </summary>
        public static void BakePathVisual(PathRoute route)
        {
            if (route == null)
            {
                Report("경로가 없어 경로 비주얼을 만들 수 없음");
                return;
            }

            if (route.PointCount < 2)
            {
                Report("웨이포인트가 2개 미만이라 경로 비주얼을 만들 수 없음");
                return;
            }

            const float PathWidth = 1.6f;
            const float TileHeight = 0.02f;

            var existing = GameObject.Find("PathVisual");

            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing);

            var root = new GameObject("PathVisual");
            root.isStatic = true;

            var pathMat = EnsureMaterial("Mat_Path", new Color(0.62f, 0.56f, 0.42f));
            var startMat = EnsureMaterial("Mat_PathStart", new Color(0.35f, 0.75f, 0.4f));
            var endMat = EnsureMaterial("Mat_PathEnd", new Color(0.8f, 0.3f, 0.3f));

            for (int i = 0; i < route.PointCount - 1; i++)
            {
                Vector3 a = route.GetPoint(i);
                Vector3 b = route.GetPoint(i + 1);
                Vector3 dir = b - a;
                float length = dir.magnitude;

                if (length < 0.01f)
                    continue;

                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = $"PathSegment_{i:00}";
                UnityEngine.Object.DestroyImmediate(tile.GetComponent<Collider>());

                tile.transform.SetParent(root.transform);
                tile.transform.position = (a + b) * 0.5f + Vector3.up * (TileHeight * 0.5f);
                tile.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

                // 길이에 폭을 더해 꺾이는 모서리를 메운다
                tile.transform.localScale = new Vector3(PathWidth, TileHeight, length + PathWidth);
                tile.GetComponent<MeshRenderer>().sharedMaterial = pathMat;
            }

            CreatePathMarker(root.transform, "SpawnMarker", route.GetPoint(0), startMat, PathWidth);
            CreatePathMarker(root.transform, "ExitMarker", route.GetPoint(route.PointCount - 1), endMat, PathWidth);

            Report("경로 비주얼 베이크 완료");
        }

        static void CreatePathMarker(Transform parent, string name, Vector3 position, Material material, float width)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = name;
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());

            marker.transform.SetParent(parent);
            marker.transform.position = position + Vector3.up * 0.03f;
            marker.transform.localScale = new Vector3(width, 0.06f, width);
            marker.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// 타워 슬롯을 이름 기준으로 하나씩 보장한다. 일부만 지워진 씬도 빠진 것만 복구한다.
        /// 슬롯 루트는 스케일 1을 유지하고 납작한 판은 자식 Visual이 담당한다
        /// (루트가 비균등 스케일이면 그 위에 세운 타워/병사가 찌그러진다).
        /// </summary>
        static void SetupSlots()
        {
            var slotRoot = GameObject.Find("Slots");

            if (slotRoot == null)
                slotRoot = new GameObject("Slots");

            var slotMat = EnsureMaterial("Mat_Slot", new Color(0.55f, 0.5f, 0.35f));
            var ringMat = EnsureMaterial("Mat_SlotRing", new Color(1f, 0.9f, 0.3f));
            var rangeMat = EnsureRangeMaterial();

            for (int i = 0; i < DefaultSlotPositions.Length; i++)
            {
                string slotName = $"Slot_{i + 1:00}";
                var existing = slotRoot.transform.Find(slotName);

                if (existing != null)
                {
                    // 루트가 과거 버전(비균등 스케일 큐브)이면 새 구조로 교체한다
                    if (existing.GetComponent<MeshFilter>() == null && existing.localScale == Vector3.one)
                        continue;

                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }

                CreateSlot(slotRoot.transform, slotName, DefaultSlotPositions[i], slotMat, ringMat, rangeMat);
            }
        }

        static void CreateSlot(Transform parent, string name, Vector3 position,
            Material slotMat, Material ringMat, Material rangeMat)
        {
            var slot = new GameObject(name);
            slot.transform.SetParent(parent);
            slot.transform.position = position;

            slot.AddComponent<TowerSlot>();

            // 클릭 판정용 콜라이더는 루트에 둔다 (자식 비주얼은 콜라이더 없음)
            var collider = slot.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.125f, 0f);
            collider.size = new Vector3(1f, 0.25f, 1f);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(slot.transform);
            visual.transform.localPosition = new Vector3(0f, 0.125f, 0f);
            visual.transform.localScale = new Vector3(1f, 0.25f, 1f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = slotMat;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ring.name = "SelectionRing";
            UnityEngine.Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(slot.transform);
            ring.transform.localPosition = new Vector3(0f, 0.27f, 0f);
            ring.transform.localScale = new Vector3(1.3f, 0.04f, 1.3f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
            ring.SetActive(false);

            // 사거리 원판: 실린더 기본 지름 1 - 런타임에 지름만큼 스케일한다
            var range = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            range.name = "RangeIndicator";
            UnityEngine.Object.DestroyImmediate(range.GetComponent<Collider>());
            range.transform.SetParent(slot.transform);
            range.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            range.transform.localScale = new Vector3(1f, 0.01f, 1f);
            range.GetComponent<MeshRenderer>().sharedMaterial = rangeMat;
            range.SetActive(false);
        }

        /// <summary>사거리 원판용 반투명 머티리얼.</summary>
        static Material EnsureRangeMaterial()
        {
            return EnsureTranslucentMaterial("Mat_Range", new Color(0.35f, 0.8f, 1f, 0.22f));
        }

        /// <summary>반투명 머티리얼 (URP Unlit 트랜스페어런트).</summary>
        static Material EnsureTranslucentMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
                return existing;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            var mat = new Material(shader);

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);

            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);

            // URP 트랜스페어런트 설정 (표면 타입 + 블렌드 + ZWrite off)
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            AssetDatabase.CreateAsset(mat, path);

            return mat;
        }

        static StageController SetupStageController(PathRoute path)
        {
            var stageGo = GameObject.Find("Stage");

            if (stageGo == null)
                stageGo = new GameObject("Stage");

            var stage = stageGo.GetComponent<StageController>();

            if (stage == null)
                stage = stageGo.AddComponent<StageController>();

            var spawner = stageGo.GetComponent<WaveSpawner>();

            if (spawner == null)
                spawner = stageGo.AddComponent<WaveSpawner>();

            var rewards = stageGo.GetComponent<RewardSystem>();

            if (rewards == null)
                rewards = stageGo.AddComponent<RewardSystem>();

            var stageData = AssetDatabase.LoadAssetAtPath<StageData>($"{DataStages}/Stage01.asset");
            var difficulty = AssetDatabase.LoadAssetAtPath<DifficultyPreset>($"{DataDifficulty}/Difficulty_Normal.asset");
            var rewardConfig = AssetDatabase.LoadAssetAtPath<RewardFlowConfig>($"{DataRewards}/RewardFlowConfig.asset");

            // 비어 있는 참조만 채운다 (사용자가 바꿔 둔 난이도 등을 덮어쓰지 않음)
            var so = new SerializedObject(stage);
            FillIfEmpty(so, "_stageData", stageData);
            FillIfEmpty(so, "_difficulty", difficulty);
            FillIfEmpty(so, "_path", path);
            FillIfEmpty(so, "_spawner", spawner);
            FillIfEmpty(so, "_rewards", rewards);
            so.ApplyModifiedPropertiesWithoutUndo();

            var rewardSo = new SerializedObject(rewards);
            FillIfEmpty(rewardSo, "_stage", stage);
            FillIfEmpty(rewardSo, "_config", rewardConfig);
            rewardSo.ApplyModifiedPropertiesWithoutUndo();

            return stage;
        }

        static void FillIfEmpty(SerializedObject so, string propertyPath, UnityEngine.Object value)
        {
            var property = so.FindProperty(propertyPath);

            if (property == null)
            {
                Report($"필드 {propertyPath}를 찾을 수 없음 - 스크립트와 셋업 코드가 어긋남");
                return;
            }

            if (property.objectReferenceValue != null)
                return;

            property.objectReferenceValue = value;
        }

        static void SetupGameUI(StageController stage)
        {
            var uiGo = GameObject.Find("GameUI");

            if (uiGo == null)
                uiGo = new GameObject("GameUI");

            var doc = uiGo.GetComponent<UIDocument>();

            if (doc == null)
                doc = uiGo.AddComponent<UIDocument>();

            // 사용자가 지정한 커스텀 PanelSettings를 덮어쓰지 않는다
            if (doc.panelSettings == null)
                doc.panelSettings = CreatePanelSettings();

            var hud = EnsureComponent<GameHUD>(uiGo);
            var buildMenu = EnsureComponent<BuildMenu>(uiGo);
            var dashboard = EnsureComponent<DebugDashboard>(uiGo);
            var rewardOverlay = EnsureComponent<RewardOverlay>(uiGo);
            EnsureComponent<MonsterHealthOverlay>(uiGo);

            var hudSo = new SerializedObject(hud);
            FillIfEmpty(hudSo, "_stage", stage);
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            var overlaySo = new SerializedObject(rewardOverlay);
            FillIfEmpty(overlaySo, "_stage", stage);
            FillIfEmpty(overlaySo, "_rewards", stage.GetComponent<RewardSystem>());
            overlaySo.ApplyModifiedPropertiesWithoutUndo();

            var dashSo = new SerializedObject(dashboard);
            FillIfEmpty(dashSo, "_stage", stage);
            dashSo.ApplyModifiedPropertiesWithoutUndo();

            var menuSo = new SerializedObject(buildMenu);
            FillIfEmpty(menuSo, "_stage", stage);

            // 카탈로그는 비어 있을 때만 채운다 (수동 구성 존중)
            var catalog = menuSo.FindProperty("_towerCatalog");
            string[] towerAssets = { "Tower_Infantry", "Tower_Archer", "Tower_Mage", "Tower_Artillery" };

            if (catalog.arraySize == 0)
            {
                catalog.arraySize = towerAssets.Length;

                for (int i = 0; i < towerAssets.Length; i++)
                {
                    var data = AssetDatabase.LoadAssetAtPath<TowerData>($"{DataTowers}/{towerAssets[i]}.asset");
                    catalog.GetArrayElementAtIndex(i).objectReferenceValue = data;
                }
            }

            menuSo.ApplyModifiedPropertiesWithoutUndo();
        }

        static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();

            if (component != null)
                return component;

            return go.AddComponent<T>();
        }

        static void AddSceneToBuildSettings()
        {
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.path == ScenePath)
                    return;
            }

            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };

            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---------- 9. 검증 ----------

        public static List<string> Validate()
        {
            var issues = new List<string>();

            ValidateTowerData(issues);
            ValidateMonsterData(issues);
            ValidateStageData(issues);
            ValidateRewardData(issues);
            ValidateScene(issues);

            if (issues.Count == 0)
                Report("검증 통과: 문제 없음");
            else
                Report($"검증 완료: 문제 {issues.Count}건");

            return issues;
        }

        static void ValidateTowerData(List<string> issues)
        {
            string[] names = { "Tower_Infantry", "Tower_Archer", "Tower_Mage", "Tower_Artillery" };

            foreach (var name in names)
            {
                var data = AssetDatabase.LoadAssetAtPath<TowerData>($"{DataTowers}/{name}.asset");

                if (data == null)
                {
                    issues.Add($"[데이터] {name}.asset 없음");
                    continue;
                }

                if (data.Levels == null || data.Levels.Length != 4)
                    issues.Add($"[데이터] {name}: 레벨이 4단계가 아님");

                if (data.TowerPrefab == null)
                    issues.Add($"[데이터] {name}: TowerPrefab 비어 있음");
                else if (data.TowerPrefab.GetComponent<Tower>() == null)
                    issues.Add($"[데이터] {name}: 프리팹에 Tower 컴포넌트 없음");

                bool needsProjectile = data.Type != TowerType.Infantry;

                if (needsProjectile && data.ProjectilePrefab == null)
                    issues.Add($"[데이터] {name}: ProjectilePrefab 비어 있음");

                if (needsProjectile && data.ImpactPrefab == null)
                    issues.Add($"[데이터] {name}: ImpactPrefab 비어 있음 (착탄 연출 없음)");

                if (needsProjectile && (data.Motion == null || !data.Motion.IsConfigured))
                    issues.Add($"[데이터] {name}: 공격 연출 미설정 - 프리셋 적용 필요");

                if (needsProjectile && data.ProjectileSpeed <= 0f)
                    issues.Add($"[데이터] {name}: ProjectileSpeed가 0 이하");

                if (data.Type == TowerType.Infantry && data.SoldierPrefab == null)
                    issues.Add($"[데이터] {name}: SoldierPrefab 비어 있음");

                if (data.Levels == null)
                    continue;

                // 0 이하 간격은 매 프레임 공격/발사체 폭주를 만든다
                for (int i = 0; i < data.Levels.Length; i++)
                {
                    var level = data.Levels[i];

                    if (level.Range <= 0f)
                        issues.Add($"[데이터] {name} Lv{i + 1}: Range가 0 이하");

                    if (level.AttackInterval <= 0f)
                        issues.Add($"[데이터] {name} Lv{i + 1}: AttackInterval이 0 이하");

                    if (data.Type != TowerType.Infantry)
                        continue;

                    if (level.SoldierCount <= 0)
                        issues.Add($"[데이터] {name} Lv{i + 1}: SoldierCount가 0 이하");

                    if (level.SoldierAttackInterval <= 0f)
                        issues.Add($"[데이터] {name} Lv{i + 1}: SoldierAttackInterval이 0 이하");
                }
            }
        }

        static void ValidateMonsterData(List<string> issues)
        {
            var guids = AssetDatabase.FindAssets("t:MonsterData", new[] { DataMonsters });

            if (guids.Length == 0)
            {
                issues.Add("[데이터] 몬스터 데이터가 하나도 없음");
                return;
            }

            foreach (var guid in guids)
            {
                var data = AssetDatabase.LoadAssetAtPath<MonsterData>(AssetDatabase.GUIDToAssetPath(guid));

                if (data.Prefab == null)
                    issues.Add($"[데이터] {data.name}: Prefab 비어 있음");
                else if (data.Prefab.GetComponent<Monster>() == null)
                    issues.Add($"[데이터] {data.name}: 프리팹에 Monster 컴포넌트 없음");

                if (data.MaxHp <= 0f)
                    issues.Add($"[데이터] {data.name}: MaxHp가 0 이하");

                // 0 이하 이동속도는 출구 도달도 처치도 불가능해 승패 판정을 막는다
                if (data.MoveSpeed <= 0f)
                    issues.Add($"[데이터] {data.name}: MoveSpeed가 0 이하 (스테이지 진행 불가)");

                if (data.MeleeInterval <= 0f)
                    issues.Add($"[데이터] {data.name}: MeleeInterval이 0 이하");

                if (data.RangedDamage > 0f && data.RangedInterval <= 0f)
                    issues.Add($"[데이터] {data.name}: RangedInterval이 0 이하");

                if (data.LifeDamage <= 0)
                    issues.Add($"[데이터] {data.name}: LifeDamage가 0 이하");
            }
        }

        static void ValidateStageData(List<string> issues)
        {
            var stage = AssetDatabase.LoadAssetAtPath<StageData>($"{DataStages}/Stage01.asset");

            if (stage == null)
            {
                issues.Add("[데이터] Stage01.asset 없음");
                return;
            }

            if (stage.Waves == null || stage.Waves.Length != 10)
            {
                issues.Add("[데이터] Stage01: 웨이브가 10개가 아님");
                return;
            }

            for (int i = 0; i < stage.Waves.Length; i++)
            {
                var wave = stage.Waves[i];

                if (wave == null || wave.Entries == null || wave.Entries.Length == 0)
                {
                    issues.Add($"[데이터] Stage01: 웨이브 {i + 1}이 비어 있음");
                    continue;
                }

                foreach (var entry in wave.Entries)
                {
                    if (entry.Monster == null)
                        issues.Add($"[데이터] Stage01: 웨이브 {i + 1}에 몬스터 미지정 항목");
                }
            }
        }

        static void ValidateRewardData(List<string> issues)
        {
            var config = AssetDatabase.LoadAssetAtPath<RewardFlowConfig>($"{DataRewards}/RewardFlowConfig.asset");

            if (config == null)
            {
                issues.Add("[보상] RewardFlowConfig 없음 (보상 데이터 생성 필요)");
                return;
            }

            if (config.Cards == null || config.Cards.Length == 0)
            {
                issues.Add("[보상] 카드 풀이 비어 있음");
                return;
            }

            int enabledCount = 0;

            foreach (var card in config.Cards)
            {
                if (card == null)
                {
                    issues.Add("[보상] 카드 풀에 빈 항목이 있음");
                    continue;
                }

                if (!card.Enabled)
                    continue;

                enabledCount++;

                if (card.Effect == RewardEffectType.None)
                    issues.Add($"[보상] {card.Id}: 효과 미지정인데 활성 상태");

                if (card.Effect == RewardEffectType.DamageRangeNarrow)
                    issues.Add($"[보상] {card.Id}: 미구현 효과(피해 범위)인데 활성 상태");

                if (card.StackLimit < 1)
                    issues.Add($"[보상] {card.Id}: StackLimit이 1 미만");
            }

            if (enabledCount == 0)
                issues.Add("[보상] 활성 카드가 하나도 없음");

            float weightSum = config.WeightCommon + config.WeightRare + config.WeightHeroic + config.WeightLegendary;

            if (weightSum <= 0f)
                issues.Add("[보상] 등급 가중치 합이 0");
        }

        static void ValidateScene(List<string> issues)
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                issues.Add("[씬] Stage01 씬이 열려 있지 않음 (씬 셋업 실행 필요)");
                return;
            }

            var stage = UnityEngine.Object.FindFirstObjectByType<StageController>();

            if (stage == null)
            {
                issues.Add("[씬] StageController 없음");
                return;
            }

            var so = new SerializedObject(stage);
            string[] fields = { "_stageData", "_difficulty", "_path", "_spawner", "_rewards" };

            foreach (var field in fields)
            {
                if (so.FindProperty(field).objectReferenceValue == null)
                    issues.Add($"[씬] StageController.{field} 참조 비어 있음");
            }

            var rewards = UnityEngine.Object.FindFirstObjectByType<RewardSystem>();

            if (rewards == null)
            {
                issues.Add("[씬] RewardSystem 없음");
            }
            else
            {
                var rewardSo = new SerializedObject(rewards);

                if (rewardSo.FindProperty("_config").objectReferenceValue == null)
                    issues.Add("[씬] RewardSystem._config 참조 비어 있음");

                if (rewardSo.FindProperty("_stage").objectReferenceValue == null)
                    issues.Add("[씬] RewardSystem._stage 참조 비어 있음");
            }

            if (UnityEngine.Object.FindFirstObjectByType<RewardOverlay>() == null)
                issues.Add("[씬] RewardOverlay(GameUI) 없음");

            if (UnityEngine.Object.FindFirstObjectByType<MonsterHealthOverlay>() == null)
                issues.Add("[씬] MonsterHealthOverlay(GameUI) 없음");

            var path = UnityEngine.Object.FindFirstObjectByType<PathRoute>();

            if (path == null || path.PointCount < 2)
                issues.Add("[씬] PathRoute가 없거나 웨이포인트가 2개 미만");

            if (GameObject.Find("PathVisual") == null)
                issues.Add("[씬] 경로 비주얼(PathVisual)이 없음 - 경로 베이크 실행 필요");

            var slots = UnityEngine.Object.FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);

            if (slots.Length == 0)
                issues.Add("[씬] TowerSlot이 하나도 없음");

            foreach (var slot in slots)
            {
                var scale = slot.transform.lossyScale;

                // 비균등 스케일 슬롯 밑에 타워를 세우면 타워/병사 비주얼이 찌그러진다
                bool uniform = Mathf.Approximately(scale.x, scale.y) && Mathf.Approximately(scale.y, scale.z);

                if (!uniform)
                    issues.Add($"[씬] {slot.name}: 슬롯 스케일이 비균등 {scale} - 자식 타워가 찌그러짐");

                if (slot.transform.Find("RangeIndicator") == null)
                    issues.Add($"[씬] {slot.name}: RangeIndicator 자식 없음 (사거리 표시 불가)");

                if (slot.transform.Find("SelectionRing") == null)
                    issues.Add($"[씬] {slot.name}: SelectionRing 자식 없음");
            }

            var doc = UnityEngine.Object.FindFirstObjectByType<UIDocument>();

            if (doc == null)
                issues.Add("[씬] UIDocument(GameUI) 없음");
            else if (doc.panelSettings == null)
                issues.Add("[씬] UIDocument에 PanelSettings 미지정");
        }

        // ---------- 공용 ----------

        static T EnsureAsset<T>(string path, Action<T> initializer) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);

            if (existing != null)
                return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            initializer(asset);

            AssetDatabase.CreateAsset(asset, path);

            return asset;
        }
    }
}
