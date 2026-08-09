using System;
using System.Collections.Generic;
using System.IO;
using Rush.Combat;
using Rush.Data;
using Rush.Fx;
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

        // 게임플레이 연출 셰이더 (Assets/Shaders/Gameplay)
        public const string RangeSphereShader = "Rush/FX/Range Sphere";
        public const string SelectionRingShader = "Rush/FX/Selection Ring";
        public const string BuildGhostShader = "Rush/FX/Build Ghost";
        public const string DebrisChunkShader = "Rush/FX/Debris Chunk";
        public const string SmokePuffShader = "Rush/FX/Smoke Puff";
        public const string LuckRayShader = "Rush/FX/Luck Ray";

        /// <summary>경로 루트 하나의 기본 정의. 씬 복구 시에도 이 정의를 기준으로 보충한다.</summary>
        struct RouteDefinition
        {
            public string Id;
            public Vector3[] Waypoints;
        }

        /// <summary>
        /// 기본 경로 4루트. 시작 지점 2곳(A 좌상 / B 좌하)에서 각각 두 갈래로 갈라져
        /// 종료 지점 2곳(1 우상 / 2 우하)으로 들어간다. 네 루트는 맵 중앙에서 서로 교차한다.
        /// 배열 순서가 스폰 분배 순서이므로 시작 지점이 번갈아 나오도록 A1/B1/A2/B2로 둔다.
        /// 좌표는 씬(Stage01)에서 손으로 다듬은 배치를 되받은 값이다.
        /// </summary>
        static readonly RouteDefinition[] DefaultRoutes =
        {
            new RouteDefinition
            {
                Id = "A1",
                Waypoints = new[]
                {
                    new Vector3(-14.13f, 0f, 7.14f),
                    new Vector3(-9.73f, 0f, 1.81f),
                    new Vector3(-8.33f, 0f, -1.77f),
                    new Vector3(-4.86f, 0f, -4.48f),
                    new Vector3(0.70f, 0f, -4.40f),
                    new Vector3(5.00f, 0f, -2.00f),
                    new Vector3(6.03f, 0f, 1.15f),
                    new Vector3(7.62f, 0f, 3.50f),
                    new Vector3(12.00f, 0f, 5.50f),
                },
            },
            new RouteDefinition
            {
                Id = "B1",
                Waypoints = new[]
                {
                    new Vector3(-12.91f, 0f, -7.91f),
                    new Vector3(-11.00f, 0f, -2.00f),
                    new Vector3(-9.50f, 0f, 1.50f),
                    new Vector3(-5.74f, 0f, 4.54f),
                    new Vector3(-1.00f, 0f, 4.80f),
                    new Vector3(3.00f, 0f, 3.50f),
                    new Vector3(5.50f, 0f, 0.50f),
                    new Vector3(7.00f, 0f, -3.50f),
                    new Vector3(9.19f, 0f, -6.87f),
                    new Vector3(12.47f, 0f, -7.35f),
                },
            },
            new RouteDefinition
            {
                Id = "A2",
                Waypoints = new[]
                {
                    new Vector3(-14.13f, 0f, 7.14f),
                    new Vector3(-9.58f, 0f, 5.89f),
                    new Vector3(-3.63f, 0f, 4.96f),
                    new Vector3(-0.13f, 0f, 4.98f),
                    new Vector3(3.34f, 0f, 3.78f),
                    new Vector3(6.43f, 0f, -0.66f),
                    new Vector3(7.03f, 0f, -3.56f),
                    new Vector3(9.03f, 0f, -6.98f),
                    new Vector3(12.47f, 0f, -7.35f),
                },
            },
            new RouteDefinition
            {
                Id = "B2",
                Waypoints = new[]
                {
                    new Vector3(-12.91f, 0f, -7.91f),
                    new Vector3(-8.37f, 0f, -6.18f),
                    new Vector3(-4.98f, 0f, -4.97f),
                    new Vector3(-1.26f, 0f, -5.23f),
                    new Vector3(2.84f, 0f, -4.04f),
                    new Vector3(4.85f, 0f, -2.36f),
                    new Vector3(5.98f, 0f, 1.13f),
                    new Vector3(7.47f, 0f, 3.42f),
                    new Vector3(12.00f, 0f, 5.50f),
                },
            },
        };

        /// <summary>
        /// 기본 타워 슬롯 위치. 슬롯 루트는 항상 스케일 1 (자식 비주얼만 납작하게).
        /// 좌표는 씬(Stage01)에서 손으로 다듬은 배치를 되받은 값이며, 왼쪽에서 오른쪽 순이다.
        /// </summary>
        static readonly Vector3[] DefaultSlotPositions =
        {
            new Vector3(-11.80f, 0f, 1.34f),
            new Vector3(-9.57f, 0f, -1.32f),
            new Vector3(-9.23f, 0f, 3.24f),
            new Vector3(-7.44f, 0f, 0.95f),
            new Vector3(-4.83f, 0f, -6.98f),
            new Vector3(-3.34f, 0f, 3.32f),
            new Vector3(-1.79f, 0f, -2.96f),
            new Vector3(0.40f, 0f, 6.31f),
            new Vector3(1.20f, 0f, 2.82f),
            new Vector3(1.60f, 0f, -6.12f),
            new Vector3(3.87f, 0f, -0.12f),
            new Vector3(6.62f, 0f, 4.39f),
            new Vector3(7.00f, 0f, -6.50f),
            new Vector3(7.88f, 0f, -0.22f),
            new Vector3(9.22f, 0f, 6.35f),
            new Vector3(11.28f, 0f, -5.35f),
        };

        /// <summary>
        /// 슬롯이 경로에 이보다 가까우면 길 위에 올라탄 것으로 본다 (검증 경고).
        /// 경로 폭 1.6의 반(0.8) + 슬롯 받침 발자국 1.1의 반(0.55)이 실제로 닿기 시작하는 거리다.
        /// </summary>
        const float SlotPathClearance = 1.35f;

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

        /// <summary>
        /// HP 디버그 뷰용 평면색 머티리얼 4개 (HP 높은 구간부터).
        /// Lit을 쓰면 그림자 진 쪽 몬스터의 색이 어두워져 구간 판독이 흔들리므로 Unlit으로 만든다.
        /// </summary>
        static Material[] EnsureDebugHpMaterials()
        {
            EnsureFolder(MaterialDir);

            return new[]
            {
                EnsureUnlitMaterial("Mat_DebugHp0", new Color(0.30f, 0.85f, 0.35f)),
                EnsureUnlitMaterial("Mat_DebugHp1", new Color(0.95f, 0.85f, 0.20f)),
                EnsureUnlitMaterial("Mat_DebugHp2", new Color(0.98f, 0.55f, 0.15f)),
                EnsureUnlitMaterial("Mat_DebugHp3", new Color(0.92f, 0.20f, 0.18f)),
            };
        }

        static Material EnsureUnlitMaterial(string name, Color color)
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

            // 적 6종 + 중간 보스 3종 (스프레드시트: 적 6종)
            EnsureUnitPrefab("Monster_Militia", typeof(Monster), EnsureMaterial("Mat_MonMilitia", new Color(0.8f, 0.3f, 0.3f)), 0.7f, 0.38f);
            EnsureUnitPrefab("Monster_HeavyInfantry", typeof(Monster), EnsureMaterial("Mat_MonHeavy", new Color(0.5f, 0.22f, 0.22f)), 0.95f, 0.5f);
            EnsureUnitPrefab("Monster_Rider", typeof(Monster), EnsureMaterial("Mat_MonRider", new Color(0.9f, 0.4f, 0.55f)), 0.7f, 0.35f);
            EnsureUnitPrefab("Monster_Scout", typeof(Monster), EnsureMaterial("Mat_MonScout", new Color(0.9f, 0.55f, 0.3f)), 0.6f, 0.32f);
            EnsureUnitPrefab("Monster_EnemyMage", typeof(Monster), EnsureMaterial("Mat_MonMage", new Color(0.55f, 0.35f, 0.9f)), 0.8f, 0.42f);
            EnsureUnitPrefab("Monster_Centurion", typeof(Monster), EnsureMaterial("Mat_MonCenturion", new Color(0.35f, 0.12f, 0.12f)), 1.15f, 0.6f);
            EnsureUnitPrefab("Monster_MidBoss1", typeof(Monster), EnsureMaterial("Mat_MonMidBoss", new Color(0.25f, 0.05f, 0.05f)), 1.5f, 0.8f);
            EnsureUnitPrefab("Monster_MidBoss2", typeof(Monster), EnsureMaterial("Mat_MonMidBoss", new Color(0.25f, 0.05f, 0.05f)), 1.65f, 0.88f);
            EnsureUnitPrefab("Monster_MidBoss3", typeof(Monster), EnsureMaterial("Mat_MonMidBoss", new Color(0.25f, 0.05f, 0.05f)), 1.8f, 0.95f);
            EnsureUnitPrefab("Monster_FinalBoss", typeof(Monster), EnsureMaterial("Mat_MonFinalBoss", new Color(0.12f, 0.02f, 0.08f)), 2.2f, 1.15f);

            EnsureFxPrefabs();

            AssetDatabase.SaveAssets();

            Report("더미 프리팹(큐브) 생성 완료");
        }

        /// <summary>파티클 연출 프리팹(사망 파편 / 연기 퍼프 / 행운 발동)을 보장한다.</summary>
        public static void EnsureFxPrefabs()
        {
            var debrisMat = EnsureFxMaterial("Mat_Debris", DebrisChunkShader);
            var smokeMat = EnsureFxMaterial("Mat_SmokePuff", SmokePuffShader);

            GameObject debris = null;

            if (debrisMat != null)
                debris = EnsureDeathDebrisPrefab(debrisMat);

            if (smokeMat != null)
                EnsureSmokePuffPrefab(smokeMat);

            EnsureLuckSparkPrefab();

            LinkMonsterDeathFx(debris);
        }

        /// <summary>행운 발동 연출 프리팹. 없으면 null.</summary>
        public static GameObject LoadLuckSparkPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Fx_LuckSpark.prefab");
        }

        /// <summary>
        /// 행운 발동(C15) 연출. 금색 불티가 안쪽으로 모여든 뒤 짧은 빛기둥이 솟는다.
        /// 기둥과 불티는 같은 셰이더를 쓰되 높이 페이드만 다르게 둔 별도 머티리얼이다.
        /// </summary>
        static GameObject EnsureLuckSparkPrefab()
        {
            string path = $"{PrefabDir}/Fx_LuckSpark.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
                return existing;

            var rayMat = EnsureLuckMaterial("Mat_LuckRay", heightFade: 1f, intensity: 1.6f);
            var sparkMat = EnsureLuckMaterial("Mat_LuckSpark", heightFade: 0f, intensity: 2.2f);

            if (rayMat == null || sparkMat == null)
                return null;

            // ---------- 루트: 빛기둥 ----------
            var root = new GameObject("Fx_LuckSpark");
            var beam = root.AddComponent<ParticleSystem>();

            var beamMain = beam.main;
            beamMain.duration = 0.5f;
            beamMain.loop = false;
            beamMain.playOnAwake = false;
            beamMain.startLifetime = new ParticleSystem.MinMaxCurve(0.34f);
            beamMain.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            beamMain.simulationSpace = ParticleSystemSimulationSpace.World;
            beamMain.maxParticles = 4;

            // 원통 메시는 높이 2 / 지름 1이라, 가늘고 긴 기둥이 되도록 축마다 다르게 준다
            beamMain.startSize3D = true;
            beamMain.startSizeX = new ParticleSystem.MinMaxCurve(0.55f);
            beamMain.startSizeY = new ParticleSystem.MinMaxCurve(1.3f);
            beamMain.startSizeZ = new ParticleSystem.MinMaxCurve(0.55f);

            var beamEmission = beam.emission;
            beamEmission.rateOverTime = 0f;

            // 불티가 모일 시간을 준 뒤에 터진다
            beamEmission.SetBursts(new[] { new ParticleSystem.Burst(0.18f, 1) });

            // 원통 중심이 파티클 원점이므로 밑동이 지면에 닿도록 위로 올린다
            var beamShape = beam.shape;
            beamShape.enabled = true;
            beamShape.shapeType = ParticleSystemShapeType.Sphere;
            beamShape.radius = 0.01f;
            beamShape.position = new Vector3(0f, 1.3f, 0f);

            // 솟아오르듯 세로로만 늘어난다
            var beamSize = beam.sizeOverLifetime;
            beamSize.enabled = true;
            beamSize.separateAxes = true;
            beamSize.x = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, 0.7f)));
            beamSize.y = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.25f),
                new Keyframe(0.35f, 1f),
                new Keyframe(1f, 1.1f)));
            beamSize.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, 0.7f)));

            var beamColor = beam.colorOverLifetime;
            beamColor.enabled = true;
            beamColor.color = new ParticleSystem.MinMaxGradient(MakeFlashGradient(0.15f));

            var beamRenderer = root.GetComponent<ParticleSystemRenderer>();
            beamRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            beamRenderer.mesh = GetPrimitiveMesh(PrimitiveType.Cylinder);
            beamRenderer.sharedMaterial = rayMat;
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;
            beamRenderer.alignment = ParticleSystemRenderSpace.World;

            // ---------- 자식: 모여드는 불티 ----------
            var sparkGo = new GameObject("Sparks");
            sparkGo.transform.SetParent(root.transform, false);

            var sparks = sparkGo.AddComponent<ParticleSystem>();

            var sparkMain = sparks.main;
            sparkMain.duration = 0.5f;
            sparkMain.loop = false;
            sparkMain.playOnAwake = false;
            sparkMain.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.3f);
            sparkMain.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            sparkMain.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            sparkMain.simulationSpace = ParticleSystemSimulationSpace.World;
            sparkMain.maxParticles = 24;

            var sparkEmission = sparks.emission;
            sparkEmission.rateOverTime = 0f;
            sparkEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10, 14) });

            // 바깥 껍질에서 생겨나 안쪽으로 빨려 들어간다 (에너지가 모이는 느낌)
            var sparkShape = sparks.shape;
            sparkShape.enabled = true;
            sparkShape.shapeType = ParticleSystemShapeType.Sphere;
            sparkShape.radius = 1.0f;
            sparkShape.radiusThickness = 0f;
            sparkShape.position = new Vector3(0f, 0.9f, 0f);

            var sparkVelocity = sparks.velocityOverLifetime;
            sparkVelocity.enabled = true;
            sparkVelocity.space = ParticleSystemSimulationSpace.Local;
            sparkVelocity.radial = new ParticleSystem.MinMaxCurve(-4.5f, -6.5f);

            var sparkSize = sparks.sizeOverLifetime;
            sparkSize.enabled = true;
            sparkSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(0.6f, 1f),
                new Keyframe(1f, 0f)));

            var sparkColor = sparks.colorOverLifetime;
            sparkColor.enabled = true;
            sparkColor.color = new ParticleSystem.MinMaxGradient(MakeFlashGradient(0.2f));

            var sparkRenderer = sparkGo.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            sparkRenderer.mesh = GetPrimitiveMesh(PrimitiveType.Sphere);
            sparkRenderer.sharedMaterial = sparkMat;
            sparkRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sparkRenderer.receiveShadows = false;

            root.AddComponent<OneShotFx>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            return prefab;
        }

        /// <summary>알파가 빠르게 차올랐다 사라지는 그라디언트 (번쩍 -> 잔광).</summary>
        static Gradient MakeFlashGradient(float peakAt)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, peakAt),
                    new GradientAlphaKey(0f, 1f),
                });

            return gradient;
        }

        /// <summary>행운 연출용 가산 머티리얼. 기둥과 불티가 높이 페이드/밝기만 다르다.</summary>
        static Material EnsureLuckMaterial(string name, float heightFade, float intensity)
        {
            var mat = EnsureFxMaterial(name, LuckRayShader);

            if (mat == null)
                return null;

            if (mat.HasProperty("_HeightFade"))
                mat.SetFloat("_HeightFade", heightFade);

            if (mat.HasProperty("_Intensity"))
                mat.SetFloat("_Intensity", intensity);

            EditorUtility.SetDirty(mat);

            return mat;
        }

        /// <summary>몬스터 프리팹에 사망 파편 연출을 연결한다 (비어 있을 때만).</summary>
        static void LinkMonsterDeathFx(GameObject debrisPrefab)
        {
            if (debrisPrefab == null)
                return;

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });
            int linked = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (go == null)
                    continue;

                var monster = go.GetComponent<Monster>();

                if (monster == null)
                    continue;

                var so = new SerializedObject(monster);
                var prop = so.FindProperty("_deathFx");

                if (prop == null || prop.objectReferenceValue != null)
                    continue;

                prop.objectReferenceValue = debrisPrefab;
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(go);
                linked++;
            }

            if (linked > 0)
                Report($"몬스터 {linked}종에 사망 파편 연결");
        }

        /// <summary>연기 퍼프 프리팹. 없으면 null.</summary>
        static GameObject LoadSmokePuffPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Fx_SmokePuff.prefab");
        }

        /// <summary>
        /// 사망 파편. 작은 큐브들이 피격 방향으로 튄 뒤 중력에 떨어진다.
        /// 조각 색은 런타임에 죽은 대상의 머티리얼에서 가져온다 (OneShotFx.ApplySourceLook).
        /// </summary>
        static GameObject EnsureDeathDebrisPrefab(Material material)
        {
            string path = $"{PrefabDir}/Fx_DeathDebris.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
                return existing;

            var root = new GameObject("Fx_DeathDebris");
            var particles = root.AddComponent<ParticleSystem>();

            var main = particles.main;
            main.duration = 0.6f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;

            // 조각이 제각각 뒤집혀 있어야 부서진 느낌이 난다 (각도는 라디안)
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14, 18) });

            // 콘은 로컬 +Z로 뿜는다. OneShotFx.SetDirection이 이 축을 피격 방향에 맞춘다
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.radius = 0.14f;

            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-5f, 5f);
            rotation.y = new ParticleSystem.MinMaxCurve(-5f, 5f);
            rotation.z = new ParticleSystem.MinMaxCurve(-5f, 5f);

            // 알파로 흐려지는 대신 끝에서 작아지며 사라진다 (불투명이라 정렬 문제가 없다)
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.7f, 1f),
                new Keyframe(1f, 0.1f)));

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = GetPrimitiveMesh(PrimitiveType.Cube);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // 조각마다 알베도의 다른 지점을 뽑으려면 파티클 중심이 셰이더까지 가야 한다.
            // UV가 TEXCOORD0을 쓰므로 Center는 TEXCOORD1로 들어간다
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.Center,
            });

            root.AddComponent<OneShotFx>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            return prefab;
        }

        /// <summary>건설/스폰 순간의 연기. 구체 몇 개가 부풀었다 사라진다.</summary>
        static GameObject EnsureSmokePuffPrefab(Material material)
        {
            string path = $"{PrefabDir}/Fx_SmokePuff.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
                return existing;

            var root = new GameObject("Fx_SmokePuff");
            var particles = root.AddComponent<ParticleSystem>();

            var main = particles.main;
            main.duration = 0.7f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);

            // 살짝 떠오르게 해서 흙먼지가 퍼지는 느낌을 준다
            main.gravityModifier = -0.06f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 12) });

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.28f;

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.35f, 1f),
                new Keyframe(1f, 0.75f)));

            var color = particles.colorOverLifetime;
            color.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = GetPrimitiveMesh(PrimitiveType.Sphere);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            root.AddComponent<OneShotFx>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            return prefab;
        }

        /// <summary>빌트인 기본 도형 메시를 얻는다 (임시 오브젝트를 만들었다 지운다).</summary>
        static Mesh GetPrimitiveMesh(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;

            UnityEngine.Object.DestroyImmediate(temp);

            return mesh;
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
        const string FbxEnemy = FbxCharacter + "/Anemy";

        /// <summary>
        /// 몬스터 프리팹에 붙일 적군 모델 배선표. Height는 월드 기준 신장(m)이고,
        /// 더미 캡슐의 Visual 스케일과 무관하게 이 값이 최종 크기를 정한다.
        /// 적 모델은 5종뿐이라 라이더/중간보스는 성격이 가까운 모델을 크기만 바꿔 돌려 쓴다.
        /// </summary>
        static readonly (string Prefab, string Model, float Height)[] MonsterModels =
        {
            ("Monster_Militia", "AnemySoldier", 0.95f),
            ("Monster_Scout", "AnemyScout", 0.9f),
            ("Monster_HeavyInfantry", "AnemyHeavy", 1.05f),
            ("Monster_EnemyMage", "AnemyMagician", 1f),
            ("Monster_Centurion", "AnemyCaptain", 1.15f),
            ("Monster_Rider", "AnemyScout", 0.9f),
            ("Monster_MidBoss1", "AnemyHeavy", 1.5f),
            ("Monster_MidBoss2", "AnemyCaptain", 1.7f),
            ("Monster_MidBoss3", "AnemyCaptain", 1.9f),
            ("Monster_FinalBoss", "AnemyCaptain", 2.3f),
        };

        /// <summary>
        /// fbx 아트 모델을 프리팹에 배선한다.
        /// 타워: TierVisuals/Tier1~3 자식으로 주입하고 더미 큐브는 끈다 (레벨별 온오프는 Tower가 처리).
        /// 병사/몬스터: Visual 노드 아래에 모델을 넣고 큐브 렌더러만 끈다 (런지 모션 등 트랜스폼 유지).
        /// 재실행하면 기존 주입분을 지우고 다시 만든다.
        /// </summary>
        public static void ApplyArtModels()
        {
            BindCharacterMaterials();

            InjectTowerTiers("Tower_Archer", "archertower", 1.5f);
            InjectTowerTiers("Tower_Infantry", "barracktower", 1.5f);
            InjectTowerTiers("Tower_Mage", "magiciantower", 1.6f);
            InjectTowerTiers("Tower_Artillery", "cannontower", 1.4f);

            InjectSoldierModel();
            InjectMonsterModels();

            AssetDatabase.SaveAssets();

            Report("아트 모델 적용 완료");
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

        /// <summary>
        /// 캐릭터 fbx의 임베디드 머티리얼을 옆 폴더의 .mat으로 빼고 확산 텍스처를 물린다.
        /// 캐릭터 fbx에는 텍스처 경로가 들어있지 않아 그냥 임포트하면 전부 흰 모델로 나온다.
        /// 같은 폴더의 "{모델이름}_Diffuse" 텍스처만 짝으로 인정한다 (한 폴더에 텍스처가 여러 장 있어서).
        /// 타워(environment)가 이미 쓰고 있는 externalObjects 리맵과 같은 방식이다.
        /// </summary>
        public static void BindCharacterMaterials()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { FbxCharacter });
            int bound = 0;

            foreach (var guid in guids)
            {
                string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
                var texture = FindDiffuseTexture(fbxPath);

                if (texture == null)
                    continue;

                if (BindModelMaterials(fbxPath, texture))
                    bound++;
            }

            Report($"캐릭터 머티리얼 {bound}개 모델에 확산 텍스처 배선");
        }

        /// <summary>fbx와 같은 폴더에 있는 "{모델이름}_Diffuse" 텍스처. 파일명 대소문자는 무시한다.</summary>
        static Texture2D FindDiffuseTexture(string fbxPath)
        {
            string directory = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            string expected = Path.GetFileNameWithoutExtension(fbxPath) + "_diffuse";

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { directory }))
            {
                string texturePath = AssetDatabase.GUIDToAssetPath(guid);

                // FindAssets는 하위 폴더까지 훑으므로 같은 폴더인지 다시 확인한다
                if (Path.GetDirectoryName(texturePath).Replace('\\', '/') != directory)
                    continue;

                if (!string.Equals(Path.GetFileNameWithoutExtension(texturePath), expected, StringComparison.OrdinalIgnoreCase))
                    continue;

                return AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            }

            return null;
        }

        /// <summary>모델의 머티리얼 슬롯마다 외부 .mat을 만들어 리맵한다. 이미 리맵돼 있으면 텍스처만 갱신한다.</summary>
        static bool BindModelMaterials(string fbxPath, Texture2D texture)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;

            if (importer == null)
                return false;

            var slotNames = CollectMaterialSlotNames(importer, fbxPath);

            if (slotNames.Count == 0)
                return false;

            string directory = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            string modelName = Path.GetFileNameWithoutExtension(fbxPath);
            bool changed = false;

            foreach (var slotName in slotNames)
            {
                string materialPath = $"{directory}/{MaterialFileName(modelName, slotName)}";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

                if (material == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");

                    if (shader == null)
                        shader = Shader.Find("Standard");

                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, materialPath);
                }

                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);

                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);

                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);

                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slotName), material);
                changed = true;
            }

            if (!changed)
                return false;

            AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
            importer.SaveAndReimport();

            return true;
        }

        /// <summary>
        /// 외부 머티리얼 파일 이름. 한 폴더에 fbx가 여러 개 있고 슬롯 이름이 겹칠 수 있으므로
        /// (Blender 기본 이름 Material.001 등) 모델 이름을 항상 앞에 붙여 서로 덮어쓰지 않게 한다.
        /// </summary>
        static string MaterialFileName(string modelName, string slotName)
        {
            if (slotName == modelName)
                return $"m_{modelName}.mat";

            return $"m_{modelName}_{slotName}.mat";
        }

        /// <summary>
        /// 모델이 가진 머티리얼 슬롯 이름. 리맵 전에는 임베디드 머티리얼로, 리맵 후에는
        /// 리맵 테이블로만 보이므로 재실행해도 같은 목록이 나오게 둘 다 모은다.
        /// </summary>
        static List<string> CollectMaterialSlotNames(ModelImporter importer, string fbxPath)
        {
            var names = new List<string>();

            foreach (var entry in importer.GetExternalObjectMap())
            {
                if (entry.Key.type != typeof(Material))
                    continue;

                if (!names.Contains(entry.Key.name))
                    names.Add(entry.Key.name);
            }

            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
            {
                var material = asset as Material;

                if (material == null)
                    continue;

                if (!names.Contains(material.name))
                    names.Add(material.name);
            }

            return names;
        }

        static void InjectSoldierModel()
        {
            InjectCharacterModel("Soldier", $"{FbxCharacter}/Hero/soldier/soldier.fbx", 0.9f);
        }

        static void InjectMonsterModels()
        {
            int injected = 0;

            foreach (var binding in MonsterModels)
            {
                string fbxPath = $"{FbxEnemy}/{binding.Model}/{binding.Model}.fbx";

                if (InjectCharacterModel(binding.Prefab, fbxPath, binding.Height))
                    injected++;
            }

            Report($"몬스터 아트 모델 {injected}/{MonsterModels.Length}종 주입");
        }

        /// <summary>
        /// 캐릭터 프리팹의 Visual 노드 아래에 fbx 모델을 넣고 더미 큐브 렌더러만 끈다.
        /// Visual 트랜스폼 자체는 런지 모션 등이 계속 쓰므로 살려 둔다.
        /// 캐릭터 루트는 지면(y=0)에 서므로 모델 바닥을 지면에 맞춘다.
        /// </summary>
        static bool InjectCharacterModel(string prefabName, string fbxPath, float targetHeight)
        {
            string prefabPath = $"{PrefabDir}/{prefabName}.prefab";

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

            if (model == null)
            {
                Report($"{prefabName}: {fbxPath} 없음 - 건너뜀");
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Report($"{prefabName}: 프리팹이 없어 건너뜀");
                return false;
            }

            var contents = PrefabUtility.LoadPrefabContents(prefabPath);

            // 중간에 예외가 나도 격리 씬이 남지 않게 언로드는 finally에서만 한다
            try
            {
                var visual = contents.transform.Find("Visual");

                if (visual == null)
                {
                    Report($"{prefabName}: Visual 노드가 없어 건너뜀");
                    return false;
                }

                var oldModel = visual.Find("Model");

                if (oldModel != null)
                    UnityEngine.Object.DestroyImmediate(oldModel.gameObject);

                var dummyRenderer = visual.GetComponent<MeshRenderer>();

                if (dummyRenderer != null)
                    dummyRenderer.enabled = false;

                var wrapper = new GameObject("Model");
                wrapper.transform.SetParent(visual);
                wrapper.transform.localPosition = Vector3.zero;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, wrapper.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                // Blender Z-up으로 뽑힌 캐릭터는 누운 채로 들어온다. 세워 두면 정면이 +Z(유니티 전방)가 된다.
                bool standUp = IsLyingDown(instance.transform);

                if (standUp)
                    instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

                NormalizeToHeight(instance.transform, targetHeight, bottomY: 0f);
                StripColliders(instance);

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);

                string posture = standUp ? ", Z-up 모델이라 세움" : "";
                Report($"{prefabName}: 모델 주입 ({Path.GetFileNameWithoutExtension(fbxPath)}, 신장 {targetHeight:F2}m{posture})");

                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// 누워서 들어온 모델인지 (Blender 기본 Z-up 내보내기).
        /// 서 있는 캐릭터는 키가 두께보다 훨씬 크므로, 세로축 길이가 Z에 몰려 있으면 누운 것으로 본다.
        /// </summary>
        static bool IsLyingDown(Transform instance)
        {
            var bounds = CalculateRendererBounds(instance);

            return bounds.size.z > bounds.size.y * 1.5f;
        }

        /// <summary>
        /// 렌더러 바운드 기준으로 목표 신장에 맞게 균등 스케일하고,
        /// 바닥이 월드 bottomY, 수평 중심이 부모 원점에 오도록 위치를 보정한다.
        /// 프리팹 편집 콘텐츠는 원점에 놓이므로 여기서의 월드 좌표는 곧 프리팹 로컬 좌표다.
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

            Vector3 pivot = Vector3.zero;

            if (instance.parent != null)
                pivot = instance.parent.position;

            Vector3 worldOffset = new Vector3(pivot.x - bounds.center.x, bottomY - bounds.min.y, pivot.z - bounds.center.z);

            instance.localPosition += ToParentScale(instance.parent, worldOffset);
        }

        /// <summary>
        /// 월드 보정량을 부모 로컬 단위로 환산한다. Visual 노드처럼 부모가 축소되어 있으면
        /// 월드 오프셋을 로컬에 그대로 더할 수 없다. (배선 대상 부모는 회전이 없어 성분별 나눗셈으로 충분)
        /// </summary>
        static Vector3 ToParentScale(Transform parent, Vector3 worldOffset)
        {
            if (parent == null)
                return worldOffset;

            Vector3 scale = parent.lossyScale;

            if (Mathf.Abs(scale.x) < 0.0001f || Mathf.Abs(scale.y) < 0.0001f || Mathf.Abs(scale.z) < 0.0001f)
                return worldOffset;

            return new Vector3(worldOffset.x / scale.x, worldOffset.y / scale.y, worldOffset.z / scale.z);
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

        /// <summary>
        /// 타워 4계열. 가격/병영 유닛 스탯은 스프레드시트(타워 시트/병영 시트)가 원본이며
        /// 셋업을 다시 실행하면 항상 시트 값으로 재베이크한다. 프리팹/연출 참조는 비어 있을 때만 채운다.
        /// 시트에 없는 전투 수치(피해/사거리/공속)는 기존 튜닝 값을 유지한다.
        /// </summary>
        public static void CreateTowerData()
        {
            CreateDummyPrefabs();

            var infantry = EnsureTower("Tower_Infantry", data =>
            {
                data.Type = TowerType.Infantry;
                data.DamageType = DamageType.Physical;
                data.Levels = new[]
                {
                    new TowerLevelStat { DisplayName = "병영 1단계", Cost = 70, Range = 2.5f, AttackInterval = 1f, SoldierCount = 3, SoldierHp = 80, SoldierDamage = 4, SoldierDamageMax = 7, SoldierAttackInterval = 1f, SoldierRespawnSeconds = 15f },
                    new TowerLevelStat { DisplayName = "병영 2단계", Cost = 115, Range = 2.5f, AttackInterval = 1f, SoldierCount = 3, SoldierHp = 130, SoldierDamage = 7, SoldierDamageMax = 12, SoldierAttackInterval = 1f, SoldierRespawnSeconds = 15f },
                    new TowerLevelStat { DisplayName = "병영 3단계", Cost = 185, Range = 2.8f, AttackInterval = 1f, SoldierCount = 3, SoldierHp = 200, SoldierDamage = 10, SoldierDamageMax = 18, SoldierAttackInterval = 1f, SoldierRespawnSeconds = 15f },
                };

                // 기사단: 높은 방어/체력, 전선 유지 특화 (방어/저항 1단계 = 피해 25% 감쇄 근사)
                data.BranchA = new TowerBranchDef
                {
                    Name = "제국 친위대 (기사단)",
                    Stat = new TowerLevelStat { DisplayName = "기사단", Cost = 330, Range = 3f, AttackInterval = 1f, SoldierCount = 3, SoldierHp = 330, SoldierDamage = 13, SoldierDamageMax = 25, SoldierAttackInterval = 1f, SoldierRespawnSeconds = 15f, SoldierDamageCut = 0.25f },
                    Skills = new[]
                    {
                        Skill("신성한 의무", "유닛의 체력이 1 이하로 떨어지면 즉시 체력 100% 회복 · 쿨타임 60/45/30초", BranchSkillType.HolyDuty, 600, 60f, 45f, 30f),
                        Skill("신성한 강타", "10/15/20% 확률로 공격이 2배 피해를 광역으로 입힘", BranchSkillType.HolySmite, 750, 2f, 2f, 2f, 0.10f, 0.15f, 0.20f),
                        Skill("굳은 의지", "최대 체력 +40/80/120 · 3레벨에 방어력 1단계 추가", BranchSkillType.IronWill, 700, 40f, 80f, 120f),
                    },
                };

                // 용병단: 빠른 충원, 낮은 방어, 높은 공격력
                data.BranchB = new TowerBranchDef
                {
                    Name = "영웅 용병단",
                    Stat = new TowerLevelStat { DisplayName = "용병단", Cost = 330, Range = 3f, AttackInterval = 1f, SoldierCount = 3, SoldierHp = 210, SoldierDamage = 24, SoldierDamageMax = 40, SoldierAttackInterval = 1f, SoldierRespawnSeconds = 15f },
                    Skills = new[]
                    {
                        Skill("현상금 수거", "용병이 적을 죽이면 골드의 1.4/1.7/2배 획득 (올림) · 전장 회수와 합연산", BranchSkillType.BountyCollect, 750, 1.4f, 1.7f, 2f),
                        Skill("빠른 충원", "부활 대기 시간 2/4/6초 감소", BranchSkillType.FastRecruit, 450, 2f, 4f, 6f),
                        Skill("장비 개조", "공격력 +4/7/10 · 3레벨에 15% 확률로 방어력 무시 공격", BranchSkillType.GearMod, 610, 4f, 7f, 10f),
                    },
                };
            });

            var archer = EnsureTower("Tower_Archer", data =>
            {
                data.Type = TowerType.Archer;
                data.DamageType = DamageType.Physical;
                data.ProjectileSpeed = 14f;
                data.Levels = new[]
                {
                    new TowerLevelStat { DisplayName = "궁수탑 1단계", Cost = 70, Damage = 8, Range = 5f, AttackInterval = 0.9f },
                    new TowerLevelStat { DisplayName = "궁수탑 2단계", Cost = 110, Damage = 12, Range = 5.5f, AttackInterval = 0.9f },
                    new TowerLevelStat { DisplayName = "궁수탑 3단계", Cost = 180, Damage = 16, Range = 5.5f, AttackInterval = 0.7f },
                };

                // 명사수 성지: 넓은 사거리 + 빠른 연사
                data.BranchA = new TowerBranchDef
                {
                    Name = "명사수 성지",
                    Stat = new TowerLevelStat { DisplayName = "명사수 성지", Cost = 320, Damage = 20, Range = 6.5f, AttackInterval = 0.55f },
                    Skills = new[]
                    {
                        Skill("헤드샷", "공격 60번마다 다음 공격에 200/400/600 추가 물리 피해 · 일반 몬스터는 10/15/20% 확률로 즉사", BranchSkillType.Headshot, 750, 200f, 400f, 600f, 0.10f, 0.15f, 0.20f),
                        Skill("정확한 조준", "크리티컬 확률 +10/20/30% · 크리티컬은 2배 피해", BranchSkillType.CriticalAim, 600, 0f, 0f, 0f, 0.10f, 0.20f, 0.30f),
                    },
                };

                // 불법 총포상: 활 대신 총. 공속은 느리나 매우 넓은 사거리와 강한 단발
                data.BranchB = new TowerBranchDef
                {
                    Name = "불법 총포상",
                    Stat = new TowerLevelStat { DisplayName = "불법 총포상", Cost = 320, Damage = 45, Range = 7.5f, AttackInterval = 1.6f },
                    Skills = new[]
                    {
                        Skill("마개조 기관총", "15초마다 1/3/5초간 공격주기 80% 감소 (공격속도 500%)", BranchSkillType.MachineGunBurst, 750, 1f, 3f, 5f),
                        Skill("급조 철갑탄", "공격 시 10/20/30% 확률로 대상의 물리 방어력 1단계 영구 감소", BranchSkillType.ArmorShredShot, 900, 0f, 0f, 0f, 0.10f, 0.20f, 0.30f),
                    },
                };
            });

            var mage = EnsureTower("Tower_Mage", data =>
            {
                data.Type = TowerType.Mage;
                data.DamageType = DamageType.Magical;
                data.ProjectileSpeed = 10f;
                data.Levels = new[]
                {
                    new TowerLevelStat { DisplayName = "마법사탑 1단계", Cost = 100, Damage = 12, Range = 4.5f, AttackInterval = 1.4f, SlowPercent = 0.25f, SlowDuration = 2f },
                    new TowerLevelStat { DisplayName = "마법사탑 2단계", Cost = 165, Damage = 18, Range = 4.5f, AttackInterval = 1.4f, SlowPercent = 0.25f, SlowDuration = 2.5f },
                    new TowerLevelStat { DisplayName = "마법사탑 3단계", Cost = 265, Damage = 26, Range = 5f, AttackInterval = 1.4f, SlowPercent = 0.3f, SlowDuration = 2.5f },
                };

                // 흑마법사: 공속은 더 느려지나 한 방이 매우 강함
                data.BranchA = new TowerBranchDef
                {
                    Name = "흑마법사",
                    Stat = new TowerLevelStat { DisplayName = "흑마법사", Cost = 420, Damage = 46, Range = 5f, AttackInterval = 1.7f, SlowPercent = 0.3f, SlowDuration = 2.5f },
                    Skills = new[]
                    {
                        Skill("죽음의 광선", "20초마다 다음 기본 공격이 사거리 내 체력이 가장 많은 적에게 300/650/1000 마법 피해", BranchSkillType.DeathRay, 850, 300f, 650f, 1000f),
                        Skill("피의 향연", "기본 공격으로 적을 제거하면 다음 기본 공격 피해 +15/20/25% · 중첩 없음 · 죽음의 광선에도 적용", BranchSkillType.BloodFeast, 600, 0.15f, 0.20f, 0.25f),
                    },
                };

                // 환영술사: 군중제어 특화
                data.BranchB = new TowerBranchDef
                {
                    Name = "환영술사",
                    Stat = new TowerLevelStat { DisplayName = "환영술사", Cost = 420, Damage = 30, Range = 5.5f, AttackInterval = 1.3f, SlowPercent = 0.4f, SlowDuration = 3f },
                    Skills = new[]
                    {
                        Skill("길잃은 방랑자", "기본 공격이 1/2/4% 확률로 적을 시작 지점으로 되돌려보냄 · 중간 보스급 이상 제외", BranchSkillType.LostWanderer, 800, 0f, 0f, 0f, 0.01f, 0.02f, 0.04f),
                        Skill("정신차려!", "30초마다 사거리 내 5/7/9명의 적이 3초간 서로를 공격 · 적의 공격은 마법사의 공격으로 간주", BranchSkillType.SnapOut, 750, 5f, 7f, 9f),
                    },
                };
            });

            var artillery = EnsureTower("Tower_Artillery", data =>
            {
                data.Type = TowerType.Artillery;
                data.DamageType = DamageType.Physical;
                data.ProjectileSpeed = 8f;
                data.Levels = new[]
                {
                    new TowerLevelStat { DisplayName = "포병탑 1단계", Cost = 125, Damage = 18, Range = 4.5f, AttackInterval = 2.5f, SplashRadius = 1.2f, ArmorPierce = 0.5f },
                    new TowerLevelStat { DisplayName = "포병탑 2단계", Cost = 190, Damage = 28, Range = 4.5f, AttackInterval = 2.5f, SplashRadius = 1.4f, ArmorPierce = 0.5f },
                    new TowerLevelStat { DisplayName = "포병탑 3단계", Cost = 300, Damage = 40, Range = 5f, AttackInterval = 2.5f, SplashRadius = 1.6f, ArmorPierce = 0.5f },
                };

                // 용의 숨결포: 광역 피해 강화
                data.BranchA = new TowerBranchDef
                {
                    Name = "용의 숨결포",
                    Stat = new TowerLevelStat { DisplayName = "용의 숨결포", Cost = 460, Damage = 52, Range = 5f, AttackInterval = 2.5f, SplashRadius = 2.1f, ArmorPierce = 0.5f },
                    Skills = new[]
                    {
                        Skill("용의 숨결", "6초마다 다음 공격이 광역 범위에 화염 장판을 남김 · 3초간 매초 20/35/50 피해 (광역 태그 아님)", BranchSkillType.DragonBreath, 600, 20f, 35f, 50f),
                        Skill("용암포탄", "기본 공격의 광역 범위 +10/20/30% · 적을 20% 둔화", BranchSkillType.LavaShell, 600, 0.10f, 0.20f, 0.30f),
                    },
                };

                // 마개조 장사정포: 광역은 약해지나 장거리 공격 가능
                data.BranchB = new TowerBranchDef
                {
                    Name = "마개조 장사정포",
                    Stat = new TowerLevelStat { DisplayName = "마개조 장사정포", Cost = 460, Damage = 48, Range = 7f, AttackInterval = 2.5f, SplashRadius = 1.3f, ArmorPierce = 0.5f },
                    Skills = new[]
                    {
                        Skill("활강포탄", "먼 거리의 적을 공격할 때 피해 +10/20/30%까지 증가", BranchSkillType.GlideShell, 600, 0.10f, 0.20f, 0.30f),
                        Skill("집속로켓", "12초마다 다음 기본 공격이 사거리 내 무작위 적에게 3/5/7발 동시 발사 · 1초간 기절", BranchSkillType.ClusterRocket, 750, 3f, 5f, 7f),
                    },
                };
            });

            // 프리팹 참조는 비어 있을 때만 채운다 (수동 교체 존중)
            WireTowerPrefabs(infantry, "Tower_Infantry", null, "Soldier", null);
            WireTowerPrefabs(archer, "Tower_Archer", "Projectile_Arrow", null, "Impact_Arrow");
            WireTowerPrefabs(mage, "Tower_Mage", "Projectile_Magic", null, "Impact_Magic");
            WireTowerPrefabs(artillery, "Tower_Artillery", "Projectile_Shell", null, "Impact_Shell");

            ApplyMotionPresets(force: false);

            AssetDatabase.SaveAssets();

            Report("타워 데이터 재베이크 완료 (시트 가격표 반영)");
        }

        /// <summary>타워 데이터를 항상 시트 값으로 덮어쓴다 (프리팹/연출 참조는 별도 규칙).</summary>
        static TowerData EnsureTower(string assetName, Action<TowerData> setup)
        {
            var data = EnsureAsset<TowerData>($"{DataTowers}/{assetName}.asset", d => { });

            setup(data);
            EditorUtility.SetDirty(data);

            return data;
        }

        /// <summary>분기 스킬 정의. 레벨별 가격은 총액의 20/32/48% (BranchSkillDef.CostOfLevel).</summary>
        static BranchSkillDef Skill(string name, string description, BranchSkillType type, int totalCost,
            float v1, float v2, float v3, float c1 = 0f, float c2 = 0f, float c3 = 0f)
        {
            return new BranchSkillDef
            {
                DisplayName = name,
                Description = description,
                Type = type,
                TotalCost = totalCost,
                Values = new[] { v1, v2, v3 },
                Chances = new[] { c1, c2, c3 },
            };
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

        /// <summary>
        /// 추가 발사(확률 발사 / 처치 시 발사)의 연출 값을 채운다.
        /// 발동은 보상 C13/C14가 담당하므로 강제 켜기 토글은 꺼진 상태로 베이크한다.
        /// </summary>
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

            // 두 궤적 프리셋은 각각 독립적으로 판정한다. 강제 켜기는 항상 꺼진 상태로 되돌린다.
            if (force || !extras.ProcMotion.IsConfigured)
            {
                ApplyMissileMotion(extras.ProcMotion);
                extras.ProcEnabled = false;
                dirty = true;
            }

            if (force || !extras.OnKillMotion.IsConfigured)
            {
                ApplyDaggerMotion(extras.OnKillMotion);
                extras.OnKillEnabled = false;
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

        /// <summary>구 로스터(9종) 에셋 이름. 시트 개편으로 삭제 대상.</summary>
        static readonly string[] LegacyMonsterNames =
        {
            "Monster_Infantry", "Monster_Archer", "Monster_Tank", "Monster_Fighter",
            "Monster_MagicInfantry", "Monster_MagicArcher", "Monster_MagicTank", "Monster_MagicFighter",
            "Monster_Boss",
        };

        /// <summary>
        /// 적 이동속도 일괄 배수. 아래 시트 값은 그대로 두고 여기서만 조정한다.
        ///
        /// 시트 원본으로는 경로 평균 길이 35유닛을 정찰병이 11초, 민병이 22초에 완주해
        /// 타워의 사거리 체류 시간이 너무 짧았다. 상대 밸런스는 그대로 두고 전체를 늦춘다.
        /// </summary>
        const float MonsterSpeedScale = 0.75f;

        /// <summary>
        /// 적 6종 + 중간 보스 3종. 스탯은 스프레드시트(적 6종/적 스탯 스케일링)가 원본이며
        /// 셋업을 다시 실행하면 항상 시트 값으로 재베이크한다.
        /// 이동속도는 민병 1.6 유닛/초를 상대값 1.0으로 둔 환산이며, MonsterSpeedScale이 곱해진다.
        /// 보스 킬 보상은 웨이브 배수를 곱해 시트의 보스 수익(165/315/600)이 되는 기본값이다.
        /// </summary>
        public static void CreateMonsterData()
        {
            CreateDummyPrefabs();
            DeleteLegacyMonsters();

            // BatchSize는 시트(배치 크기 - 스폰 단위). 마법사/백인대장은 단독 등장이라 자동 조정에서 제외된다.
            EnsureMonster("Monster_Militia", "민병", data =>
            {
                data.MaxHp = 60; data.MoveSpeed = 1.6f;
                data.PhysicalDefense = DefenseGrade.Low; data.MagicalDefense = DefenseGrade.Low;
                data.GoldReward = 5; data.MeleeDamage = 6;
                data.BatchSize = 4;
            });

            EnsureMonster("Monster_HeavyInfantry", "중보병", data =>
            {
                data.MaxHp = 140; data.MoveSpeed = 1.44f;
                data.PhysicalDefense = DefenseGrade.High; data.MagicalDefense = DefenseGrade.Low;
                data.GoldReward = 10; data.MeleeDamage = 10;
                data.BatchSize = 2;
            });

            EnsureMonster("Monster_Rider", "라이더", data =>
            {
                // 비행: 병영 저지 불가, 궁수/마법사만 공격 가능. 교전하지 않으므로 공격력 없음.
                data.MaxHp = 90; data.MoveSpeed = 2.08f; data.IsFlying = true;
                data.PhysicalDefense = DefenseGrade.Medium; data.MagicalDefense = DefenseGrade.Low;
                data.GoldReward = 15; data.MeleeDamage = 0;
                data.BatchSize = 3;
            });

            EnsureMonster("Monster_Scout", "정찰병", data =>
            {
                data.MaxHp = 70; data.MoveSpeed = 3.2f;
                data.PhysicalDefense = DefenseGrade.Low; data.MagicalDefense = DefenseGrade.Medium;
                data.GoldReward = 10; data.MeleeDamage = 20;
                data.BatchSize = 3;
            });

            EnsureMonster("Monster_EnemyMage", "마법사", data =>
            {
                // 적 중 유일하게 마법 피해로 공격한다 (병영 유닛 마법 저항이 의미를 갖는 유일한 상황)
                data.MaxHp = 160; data.MoveSpeed = 1.44f;
                data.PhysicalDefense = DefenseGrade.Low; data.MagicalDefense = DefenseGrade.Great;
                data.GoldReward = 15; data.MeleeDamage = 22;
                data.BatchSize = 1; data.AllowBatchGrowth = false;
            });

            EnsureMonster("Monster_Centurion", "백인대장", data =>
            {
                data.MaxHp = 420; data.MoveSpeed = 1.92f;
                data.PhysicalDefense = DefenseGrade.Great; data.MagicalDefense = DefenseGrade.Medium;
                data.GoldReward = 30; data.MeleeDamage = 35;
                data.BatchSize = 1; data.AllowBatchGrowth = false;
            });

            // 중간 보스는 시트(중간 보스 3종)의 웨이브별 기준 스탯을 쓴다.
            // 에셋 값에 웨이브 배수(6웨이브 1.0 / 12웨이브 1.25 / 18웨이브 1.5)가 곱해져 최종값이 되므로 나눠서 넣는다.
            // 최종 체력 450 / 2000 / 10000, 킬 보상 510 / 970 / 1850, 공격력은 시트 범위의 중앙값.
            // 방어 배분은 공성 책사 기준(물리 2단계 / 마법 2단계)이며, 3종 무작위 배정은 아직 미구현이다.
            EnsureMonster("Monster_MidBoss1", "중간 보스 1", data =>
            {
                data.MaxHp = 450; data.MoveSpeed = 0.8f;
                data.PhysicalDefense = DefenseGrade.High; data.MagicalDefense = DefenseGrade.High;
                data.GoldReward = 510; data.LifeDamage = 20; data.IsBoss = true;
                data.MeleeDamage = 40; data.MeleeInterval = 1.5f;
            });

            EnsureMonster("Monster_MidBoss2", "중간 보스 2", data =>
            {
                data.MaxHp = 1600; data.MoveSpeed = 0.8f;
                data.PhysicalDefense = DefenseGrade.High; data.MagicalDefense = DefenseGrade.High;
                data.GoldReward = 776; data.LifeDamage = 20; data.IsBoss = true;
                data.MeleeDamage = 44; data.MeleeInterval = 1.5f;
            });

            EnsureMonster("Monster_MidBoss3", "중간 보스 3", data =>
            {
                // 18웨이브는 13~18 구간이라 배수가 1.5다 (2.0 구간은 19웨이브부터)
                data.MaxHp = 6667; data.MoveSpeed = 0.8f;
                data.PhysicalDefense = DefenseGrade.High; data.MagicalDefense = DefenseGrade.High;
                data.GoldReward = 1233; data.LifeDamage = 20; data.IsBoss = true;
                data.MeleeDamage = 53; data.MeleeInterval = 1.5f;
            });

            // 최종 보스 (24웨이브, 배수 2.0). 최종 체력 30000, 공격력 80~140의 중앙값 110.
            // MoveSpeed 0.8은 시트의 상대 이동속도 0.5를 민병 기준값 1.6으로 환산한 값이다
            // (0.5 x 1.6 = 0.8). 여기에 MonsterSpeedScale이 곱해져 런타임 0.6이 되고, 민병 1.2의 절반이다.
            // 킬 보상은 없고 통과하면 라이프 100을 깎는다 (시작 20이므로 사실상 즉시 패배).
            EnsureMonster("Monster_FinalBoss", "반란군 우두머리", data =>
            {
                data.MaxHp = 15000; data.MoveSpeed = 0.8f;
                data.PhysicalDefense = DefenseGrade.High; data.MagicalDefense = DefenseGrade.High;
                data.GoldReward = 0; data.LifeDamage = 100;
                data.IsBoss = true; data.IsFinalBoss = true;
                data.MeleeDamage = 55; data.MeleeInterval = 1.5f;
            });

            AssetDatabase.SaveAssets();

            Report("몬스터 데이터 재베이크 완료 (적 6종 + 중간 보스 3종 + 최종 보스)");
        }

        static void DeleteLegacyMonsters()
        {
            int deleted = 0;

            foreach (var name in LegacyMonsterNames)
            {
                if (AssetDatabase.DeleteAsset($"{DataMonsters}/{name}.asset"))
                    deleted++;

                if (AssetDatabase.DeleteAsset($"{PrefabDir}/{name}.prefab"))
                    deleted++;
            }

            if (deleted > 0)
                Report($"구 몬스터 에셋 정리: {deleted}건 삭제");
        }

        /// <summary>스탯은 항상 시트 값으로 덮어쓴다. 프리팹 참조는 수동 교체를 존중해 비어 있을 때만 채운다.</summary>
        static void EnsureMonster(string assetName, string displayName, Action<MonsterData> setup)
        {
            var data = EnsureAsset<MonsterData>($"{DataMonsters}/{assetName}.asset", d => { });

            data.DisplayName = displayName;
            data.LifeDamage = 1;
            data.IsBoss = false;
            data.IsFinalBoss = false;
            data.IsFlying = false;
            data.MeleeInterval = 1f;
            data.RegenPerSecond = 0f;
            data.RangedDamage = 0f;
            data.BatchSize = 1;
            data.AllowBatchGrowth = true;

            setup(data);

            // 시트 값은 setup 안에 그대로 두고, 페이싱 조정은 여기 한 곳에서만 건다
            data.MoveSpeed *= MonsterSpeedScale;

            if (data.Prefab == null)
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

            var stage = EnsureAsset<StageData>($"{DataStages}/Stage01.asset", s => { });

            // 웨이브 구성은 시트가 원본이라 셋업을 다시 실행하면 항상 재베이크한다
            BuildStage01(stage);
            EditorUtility.SetDirty(stage);

            AssetDatabase.SaveAssets();

            Report("스테이지/난이도 데이터 생성 완료 (24웨이브 재베이크)");
        }

        /// <summary>웨이브별 예산. 스프레드시트(적 유닛 구성예산) B열. 23웨이브 누적 수입 32100.</summary>
        static readonly int[] WaveBudgets =
        {
            160, 200, 250, 300, 360, 730, 625, 695, 775, 865, 960, 1390,
            1195, 1330, 1480, 1650, 1835, 2660, 2280, 2535, 2825, 3145, 3505, 3905,
        };

        /// <summary>
        /// 웨이브별 적 스탯 배수 (체력/공격력/킬 보상). 스프레드시트(적 스탯 스케일링)의 계단식 값이다.
        /// 1~6 = 1.0, 7~12 = 1.25, 13~18 = 1.5, 19~24 = 2.0. 방어 단계에는 적용하지 않는다.
        /// </summary>
        static readonly float[] WaveMultipliers =
        {
            1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.25f, 1.25f, 1.25f, 1.25f, 1.25f, 1.25f,
            1.50f, 1.50f, 1.50f, 1.50f, 1.50f, 1.50f, 2.00f, 2.00f, 2.00f, 2.00f, 2.00f, 2.00f,
        };

        /// <summary>
        /// 24웨이브 구성. 1~6웨이브는 고정 구성(시트: 고정 구간), 7웨이브부터는 예산 기반 무작위.
        /// 보스 웨이브(6/12/18)는 보스 + 남은 예산의 무작위 구성으로 채워진다.
        /// 고정 구성 수량은 "단가(기본 킬 보상 x 배수) 합 = 예산"이 되도록 계산된 값이다.
        /// </summary>
        static void BuildStage01(StageData stage)
        {
            var militia = LoadMonster("Monster_Militia");
            var heavy = LoadMonster("Monster_HeavyInfantry");
            var rider = LoadMonster("Monster_Rider");
            var scout = LoadMonster("Monster_Scout");
            var mage = LoadMonster("Monster_EnemyMage");
            var centurion = LoadMonster("Monster_Centurion");
            var boss1 = LoadMonster("Monster_MidBoss1");
            var boss2 = LoadMonster("Monster_MidBoss2");
            var boss3 = LoadMonster("Monster_MidBoss3");

            stage.StartLife = 20;
            stage.StartGold = 350;
            // 시트(웨이브 타이밍): 스폰 구간 60초 + 웨이브 간 대기 20초 = 한 사이클 80초.
            // 스폰을 60초 안에 끝내는 배치 규칙은 아직 없어서 현재는 사이클 길이만 맞춘다.
            stage.FirstWaveDelay = 25f;
            stage.WaveInterval = 80f;
            stage.EarlyCallBudgetFraction = 0.15f;
            stage.RandomPool = new[] { militia, heavy, rider, scout, mage, centurion };

            // 배치 규칙 (시트: 간격 규칙 / 자동 조정)
            stage.SpawnWindow = 60f;
            stage.BatchInnerInterval = 0.35f;
            stage.MinBatchInterval = 2f;
            stage.MaxBatchGrowth = 2;

            // 최종 보스전 추가 스폰 (시트: 24웨이브 스폰 진행)
            stage.EndlessSpawnInterval = 30f;
            stage.EndlessBudgetFraction = 0.5f;
            stage.EndlessStatStep = 0.05f;

            BuildArchetypes(stage, militia, heavy, rider, scout, mage, centurion);
            BuildBossArchetypes(stage);

            stage.Waves = new WaveData[24];

            for (int i = 0; i < 24; i++)
            {
                stage.Waves[i] = new WaveData
                {
                    Budget = WaveBudgets[i],
                    StatMultiplier = WaveMultipliers[i],
                };
            }

            // 고정 구간 (1~6웨이브). 시트: 민병대 / 민병대+정찰병 / 중보병+라이더 / 마법사+민병대 / 중보병+마법사 / 중간 보스 1
            // 마릿수는 "단가 합 = 예산"이 되도록 맞췄다 (배수 1.0 구간이라 단가는 기본 킬 보상과 같다).
            // 경로는 시트(고정 구간 경로)를 따른다. 가중치(50/50, 60/40 등)는 아직 순번 분배로 근사한다.
            stage.Waves[0].RouteIds = new[] { "A1" };
            stage.Waves[0].Entries = new[]
            {
                Entry(militia, 32, 1.8f),
            };

            stage.Waves[1].RouteIds = new[] { "A1", "A2" };
            stage.Waves[1].Entries = new[]
            {
                Entry(militia, 20, 2.8f),
                Entry(scout, 10, 5f, 5f),
            };

            stage.Waves[2].RouteIds = new[] { "A1", "B1" };
            stage.Waves[2].Entries = new[]
            {
                Entry(heavy, 16, 3.4f),
                Entry(rider, 6, 8f, 4f),
            };

            stage.Waves[3].RouteIds = new[] { "B1", "B2" };
            stage.Waves[3].Entries = new[]
            {
                Entry(mage, 10, 5.5f),
                Entry(militia, 30, 1.8f, 3f),
            };

            stage.Waves[4].Entries = new[]
            {
                Entry(heavy, 18, 3f),
                Entry(mage, 12, 4.5f, 5f),
            };

            // 6웨이브: 중간 보스 1(단가 510) + 남은 예산 220을 모든 종류로 채운다
            stage.Waves[5].IsBossWave = true;
            stage.Waves[5].Entries = new[]
            {
                Entry(militia, 10, 2f),
                Entry(heavy, 5, 3f, 4f),
                Entry(rider, 2, 6f, 6f),
                Entry(scout, 3, 4f, 8f),
                Entry(mage, 2, 6f, 10f),
                Entry(centurion, 1, 1f, 14f),
                Entry(boss1, 1, 1f, 20f),
            };

            // 12/18웨이브: 보스 + 남은 예산은 무작위 구성이 자동으로 채운다
            stage.Waves[11].IsBossWave = true;
            stage.Waves[11].Entries = new[] { Entry(boss2, 1, 1f, 12f) };

            stage.Waves[17].IsBossWave = true;
            stage.Waves[17].Entries = new[] { Entry(boss3, 1, 1f, 15f) };

            // 24웨이브: 최종 보스가 웨이브 시작과 동시에 등장하고, 잡졸은 예산이 다 소진된 뒤에도 계속 나온다.
            // 최종 보스는 킬 보상이 0이라 예산을 소모하지 않으므로 잡졸 예산은 3905 전부다.
            var finalBoss = LoadMonster("Monster_FinalBoss");

            stage.Waves[23].IsFinalWave = true;
            stage.Waves[23].Entries = new[] { Entry(finalBoss, 1, 1f) };
        }

        /// <summary>
        /// 웨이브 아키타입 6종. 스프레드시트(적 유닛 스폰 시스템: 아키타입 요약/배치 규칙/경로 가중치 +
        /// 적 유닛 구성예산: 로그라이크 구간 등장확률과 예산 배분).
        /// 등장 확률은 구간별 표(7~12 / 13~18 / 19~23)를 원본으로 쓴다.
        /// </summary>
        static void BuildArchetypes(StageData stage, MonsterData militia, MonsterData heavy,
            MonsterData rider, MonsterData scout, MonsterData mage, MonsterData centurion)
        {
            stage.Archetypes = new[]
            {
                new WaveArchetypeDef
                {
                    Name = "개떼 1",
                    Members = new[] { Member(militia, 1f) },
                    BudgetScale = 0.85f,
                    BandChance = new[] { 0.10f, 0.05f, 0f },
                    RouteCountChance = new[] { 0.05f, 0.20f, 0.35f, 0.40f },
                    MinGap = 4,
                    AllowedAfterBoss = false,
                },
                new WaveArchetypeDef
                {
                    Name = "개떼 2",
                    Members = new[] { Member(militia, 0.5f), Member(heavy, 0.3f), Member(scout, 0.2f) },
                    BudgetScale = 0.80f,
                    BandChance = new[] { 0.19f, 0.17f, 0.12f },
                    RouteCountChance = new[] { 0.05f, 0.20f, 0.35f, 0.40f },
                    AllowedAfterBoss = true,
                },
                new WaveArchetypeDef
                {
                    Name = "정예 1",
                    Members = new[] { Member(heavy, 0.4f), Member(centurion, 0.6f) },
                    BudgetScale = 0.95f,
                    BandChance = new[] { 0.20f, 0.22f, 0.25f },
                    RouteCountChance = new[] { 0.30f, 0.40f, 0.20f, 0.10f },
                    AllowedAfterBoss = false,
                },
                new WaveArchetypeDef
                {
                    Name = "정예 2",
                    Members = new[] { Member(heavy, 0.3f), Member(centurion, 0.3f), Member(mage, 0.4f) },
                    BudgetScale = 1.00f,
                    BandChance = new[] { 0.16f, 0.18f, 0.25f },
                    RouteCountChance = new[] { 0.30f, 0.40f, 0.20f, 0.10f },
                    MinWave = 13,
                    AllowedAfterBoss = false,
                },
                new WaveArchetypeDef
                {
                    Name = "기동",
                    Members = new[] { Member(rider, 0.5f), Member(scout, 0.5f) },
                    BudgetScale = 1.20f,
                    BandChance = new[] { 0.17f, 0.15f, 0.15f },
                    RouteCountChance = new[] { 0.15f, 0.30f, 0.30f, 0.25f },
                    AllowedAfterBoss = false,
                },
                new WaveArchetypeDef
                {
                    // 구성원을 비워두면 RandomPool 전체를 균등 배분한다 (시트: 모든 구성 랜덤)
                    Name = "짬",
                    Members = new ArchetypeMember[0],
                    BudgetScale = 0.85f,
                    BandChance = new[] { 0.18f, 0.23f, 0.23f },
                    RouteCountChance = new[] { 0.15f, 0.30f, 0.30f, 0.25f },
                    AllowedAfterBoss = true,
                },
            };
        }

        static ArchetypeMember Member(MonsterData monster, float share)
        {
            return new ArchetypeMember { Monster = monster, Share = share };
        }

        /// <summary>
        /// 중간 보스 종류 3종. 시트(중간보스 3종: 보스 종류별 특성).
        /// 체력 배율과 방어 배분만 종류가 정하고, 나머지는 웨이브 슬롯 데이터를 쓴다.
        /// </summary>
        static void BuildBossArchetypes(StageData stage)
        {
            stage.BossArchetypes = new[]
            {
                new BossArchetypeDef
                {
                    Archetype = BossArchetype.Charger,
                    DisplayName = "돌격대장",
                    HpScale = 0.80f,
                    PhysicalDefense = DefenseGrade.Great,
                    MagicalDefense = DefenseGrade.Low,
                },
                new BossArchetypeDef
                {
                    Archetype = BossArchetype.Priest,
                    DisplayName = "타락한 사제",
                    HpScale = 0.90f,
                    PhysicalDefense = DefenseGrade.Medium,
                    MagicalDefense = DefenseGrade.Great,
                },
                new BossArchetypeDef
                {
                    Archetype = BossArchetype.Siege,
                    DisplayName = "공성 책사",
                    HpScale = 1.00f,
                    PhysicalDefense = DefenseGrade.High,
                    MagicalDefense = DefenseGrade.High,
                },
            };
        }

        static MonsterData LoadMonster(string name)
        {
            return AssetDatabase.LoadAssetAtPath<MonsterData>($"{DataMonsters}/{name}.asset");
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

        /// <summary>보상 카드 57종과 플로우 설정을 생성/동기화한다. 수치는 기존 조정값을 존중한다.</summary>
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

            // 플로우 수치는 시트가 원본이라 항상 재베이크한다 (매 웨이브 제시 / 리롤 판당 5회 / 등급 목표 확률)
            config.FirstRewardWave = 1;
            config.EveryNWaves = 1;
            config.CardsPerOffer = 3;
            config.RerollsPerRun = 5;
            config.RerollCost = 0;
            config.TargetCommon = 60.14f;
            config.TargetRare = 26.92f;
            config.TargetHeroic = 12.25f;
            config.TargetLegendary = 0.68f;

            config.Cards = cards.ToArray();
            EditorUtility.SetDirty(config);

            AssetDatabase.SaveAssets();

            Report($"보상 데이터 동기화 완료 (카드 {cards.Count}종)");

            return config;
        }

        // ---------- 7. 전체 에셋 원클릭 ----------

        /// <summary>메뉴/자동화에서 원클릭 실행 (Stage Command Center의 전체 셋업과 동일).</summary>
        [MenuItem("Rush/전체 셋업 (에셋+씬)")]
        public static void RunFullSetup()
        {
            SetupScene();
        }

        /// <summary>씬에서 손으로 배치한 경로/슬롯의 이름과 순서를 정리하고 경로 비주얼을 다시 굽는다.</summary>
        [MenuItem("Rush/씬 레이아웃 정리 (이름 + 경로 비주얼)")]
        public static void RunNormalizeSceneLayout()
        {
            NormalizeSceneLayout();
        }

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
            // 미저장 변경 확인은 씬을 실제로 교체할 때만 한다.
            // 이미 대상 씬을 열고 있으면 버려지는 변경이 없고, 셋업 마지막에 그대로 저장된다.
            // (이 확인은 모달이라 자동화에서 호출하면 응답을 못 받고 중단된다)
            bool switchesScene = EditorSceneManager.GetActiveScene().path != ScenePath;

            if (switchesScene && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
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
            var paths = SetupPaths();
            BakePathVisual(paths);
            SetupSlots();
            UpgradeSlotVisuals();
            var ghostPreview = BakeBuildGhosts();
            var stage = SetupStageController(paths);
            SetupGameUI(stage, ghostPreview);

            AddSceneToBuildSettings();

            // 에디터가 포커스를 잃어도 플레이 루프가 계속 돌게 한다 (원격/자동화 작업 시 정지 방지)
            PlayerSettings.runInBackground = true;

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Report("씬 셋업 완료: " + ScenePath);
        }

        /// <summary>
        /// 카메라가 없을 때만 만들고 기본 앵글을 준다.
        /// 이미 있으면 위치/회전/fov를 절대 건드리지 않는다 (수동으로 잡은 앵글 보존).
        /// </summary>
        static void SetupCamera()
        {
            var cam = Camera.main;

            if (cam != null)
                return;

            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();

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

        /// <summary>
        /// 경로 루트 4개와 웨이포인트를 보장한다. 루트만 있고 포인트가 모자라면 기본 정의로 보충한다.
        /// 웨이포인트를 씬에서 옮겨 뒀다면 그 위치는 건드리지 않는다 (개수만 채운다).
        /// </summary>
        static PathRoute[] SetupPaths()
        {
            // 단일 경로 시절의 최상위 "Path" 오브젝트는 4루트 구조로 대체됐다
            var legacy = GameObject.Find("Path");

            if (legacy != null && legacy.transform.parent == null)
            {
                UnityEngine.Object.DestroyImmediate(legacy);
                Report("단일 경로(Path)를 제거하고 4루트로 교체함");
            }

            var rootGo = GameObject.Find("Paths");

            if (rootGo == null)
                rootGo = new GameObject("Paths");

            var routes = new PathRoute[DefaultRoutes.Length];

            for (int i = 0; i < DefaultRoutes.Length; i++)
                routes[i] = SetupRoute(rootGo.transform, DefaultRoutes[i]);

            Report($"경로 루트 {routes.Length}개 준비 완료");

            return routes;
        }

        static PathRoute SetupRoute(Transform parent, RouteDefinition def)
        {
            string routeName = $"Path_{def.Id}";
            var routeTransform = parent.Find(routeName);

            if (routeTransform == null)
            {
                var go = new GameObject(routeName);
                go.transform.SetParent(parent);
                routeTransform = go.transform;
            }

            // 웨이포인트가 모자랄 때만 채운다. 손으로 다듬은 경로를 덮지 않는 것이 원칙이다.
            if (routeTransform.childCount < def.Waypoints.Length
                && !WarnIfPrefabInstance(routeTransform.gameObject, $"{routeName} 웨이포인트"))
            {
                for (int i = routeTransform.childCount; i < def.Waypoints.Length; i++)
                {
                    var wp = new GameObject($"P{i}");
                    wp.transform.SetParent(routeTransform);
                    wp.transform.position = def.Waypoints[i];
                }
            }

            var route = routeTransform.GetComponent<PathRoute>();

            if (route == null)
                route = routeTransform.gameObject.AddComponent<PathRoute>();

            route.SetRouteId(def.Id);
            route.CachePoints();

            EditorUtility.SetDirty(route);

            return route;
        }

        /// <summary>
        /// 경로를 바닥 타일로 베이크한다. 런타임 생성 없이 에디트 모드에서 바로 눈으로 확인할 수 있다.
        /// 다시 호출하면 기존 타일을 지우고 현재 웨이포인트 기준으로 재생성한다.
        /// </summary>
        public static void BakePathVisual(PathRoute[] routes)
        {
            if (routes == null || routes.Length == 0)
            {
                Report("경로가 없어 경로 비주얼을 만들 수 없음");
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

            // 시작/종료 지점은 루트끼리 공유하므로 마커가 겹치지 않게 좌표로 한 번만 찍는다
            var markerPoints = new List<Vector3>(4);

            // 라벨은 루트 ID가 아니라 좌표로 정한다. B1은 이름이 1로 끝나지만 종료는 2번 지점이다.
            var startPoints = CollectEndpoints(routes, start: true);
            var exitPoints = CollectEndpoints(routes, start: false);

            int baked = 0;

            foreach (var route in routes)
            {
                if (route == null || route.PointCount < 2)
                    continue;

                var group = new GameObject($"Route_{route.RouteId}");
                group.transform.SetParent(root.transform);
                group.isStatic = true;

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

                    tile.transform.SetParent(group.transform);
                    tile.transform.position = (a + b) * 0.5f + Vector3.up * (TileHeight * 0.5f);
                    tile.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

                    // 길이에 폭을 더해 꺾이는 모서리를 메운다
                    tile.transform.localScale = new Vector3(PathWidth, TileHeight, length + PathWidth);
                    tile.GetComponent<MeshRenderer>().sharedMaterial = pathMat;
                }

                Vector3 startPoint = route.GetPoint(0);
                Vector3 exitPoint = route.GetPoint(route.PointCount - 1);

                TryCreateSharedMarker(root.transform, markerPoints,
                    $"SpawnMarker_{LabelOf(startPoints, startPoint, StartLabels)}", startPoint, startMat, PathWidth);

                TryCreateSharedMarker(root.transform, markerPoints,
                    $"ExitMarker_{LabelOf(exitPoints, exitPoint, ExitLabels)}", exitPoint, endMat, PathWidth);

                baked++;
            }

            if (baked == 0)
            {
                Report("웨이포인트가 2개 이상인 루트가 없어 경로 비주얼을 만들 수 없음");
                return;
            }

            Report($"경로 비주얼 베이크 완료 (루트 {baked}개)");
        }

        /// <summary>
        /// 씬에서 손으로 배치한 경로/슬롯을 정리한다. 위치는 절대 건드리지 않고 이름과 순서만 맞춘다.
        /// 웨이포인트를 복제하거나 슬롯을 늘리면 "Slot_07 (3)" 같은 이름이 남으므로 정기적으로 돌린다.
        /// 정리 후 경로 비주얼을 현재 좌표로 다시 베이크한다.
        /// </summary>
        public static void NormalizeSceneLayout()
        {
            var routes = UnityEngine.Object.FindObjectsByType<PathRoute>(FindObjectsSortMode.None);

            if (routes.Length == 0)
            {
                Report("씬에 경로 루트가 없어 정리를 건너뜀");
                return;
            }

            int renamedWaypoints = NormalizeRouteNames(routes);
            int renamedSlots = NormalizeSlotNames();

            // 루트를 RouteId 순서(A1/B1/A2/B2)로 정렬해 베이크 순서와 계층 순서를 맞춘다
            System.Array.Sort(routes, CompareRouteOrder);

            SortRoutesInHierarchy(routes);

            BakePathVisual(routes);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Report($"씬 정리 완료 (웨이포인트 {renamedWaypoints}개 / 슬롯 {renamedSlots}개 이름 정리)");
        }

        /// <summary>루트 오브젝트 이름을 RouteId에 맞추고 웨이포인트를 P00부터 다시 번호 매긴다.</summary>
        static int NormalizeRouteNames(PathRoute[] routes)
        {
            int renamed = 0;

            foreach (var route in routes)
            {
                string expectedName = $"Path_{route.RouteId}";

                if (route.name != expectedName)
                {
                    Undo.RecordObject(route.gameObject, "Normalize route name");
                    route.name = expectedName;
                }

                var transform = route.transform;

                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    string expected = $"P{i:00}";

                    if (child.name == expected)
                        continue;

                    Undo.RecordObject(child.gameObject, "Normalize waypoint name");
                    child.name = expected;
                    renamed++;
                }

                route.CachePoints();
                EditorUtility.SetDirty(route);
            }

            return renamed;
        }

        /// <summary>
        /// 슬롯 이름을 Slot_01부터 다시 매긴다. 순서는 왼쪽 위에서 오른쪽 아래로 (x 오름차순, 같으면 z 내림차순).
        /// 복제로 생긴 "Slot_07 (3)" 같은 이름이 사라지고, 셋업/검증 로그에서 슬롯을 특정할 수 있게 된다.
        /// </summary>
        static int NormalizeSlotNames()
        {
            var slots = UnityEngine.Object.FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);

            if (slots.Length == 0)
                return 0;

            System.Array.Sort(slots, CompareSlotOrder);

            int renamed = 0;

            var previousNames = new string[slots.Length];

            // 이름이 겹치는 중간 상태를 피하려고 임시 이름을 거쳐 두 번에 나눠 바꾼다
            for (int i = 0; i < slots.Length; i++)
            {
                previousNames[i] = slots[i].name;
                slots[i].name = $"__SlotTemp_{i:00}";
            }

            for (int i = 0; i < slots.Length; i++)
            {
                string expected = $"Slot_{i + 1:00}";

                Undo.RecordObject(slots[i].gameObject, "Normalize slot name");
                slots[i].name = expected;
                slots[i].transform.SetSiblingIndex(i);

                if (previousNames[i] != expected)
                    renamed++;
            }

            return renamed;
        }

        static int CompareSlotOrder(TowerSlot a, TowerSlot b)
        {
            Vector3 pa = a.transform.position;
            Vector3 pb = b.transform.position;

            int byX = pa.x.CompareTo(pb.x);

            if (byX != 0)
                return byX;

            return pb.z.CompareTo(pa.z);
        }

        /// <summary>스폰 분배가 시작 지점을 번갈아 쓰도록 A1/B1/A2/B2 순서로 맞춘다.</summary>
        static int CompareRouteOrder(PathRoute a, PathRoute b)
        {
            return RouteOrderKey(a.RouteId).CompareTo(RouteOrderKey(b.RouteId));
        }

        static int RouteOrderKey(string routeId)
        {
            for (int i = 0; i < DefaultRoutes.Length; i++)
            {
                if (DefaultRoutes[i].Id == routeId)
                    return i;
            }

            return int.MaxValue;
        }

        static void SortRoutesInHierarchy(PathRoute[] ordered)
        {
            for (int i = 0; i < ordered.Length; i++)
                ordered[i].transform.SetSiblingIndex(i);
        }

        /// <summary>기획 스케치의 지점 표기. 시작은 위(A)에서 아래(B), 종료는 위(1)에서 아래(2) 순이다.</summary>
        static readonly string[] StartLabels = { "A", "B" };
        static readonly string[] ExitLabels = { "1", "2" };

        /// <summary>
        /// 루트들의 시작점(또는 종료점)을 좌표로 묶어 위에서 아래 순으로 돌려준다.
        /// 여러 루트가 한 지점을 공유하므로 중복은 제거한다.
        /// </summary>
        static List<Vector3> CollectEndpoints(PathRoute[] routes, bool start)
        {
            var points = new List<Vector3>(4);

            foreach (var route in routes)
            {
                if (route == null || route.PointCount < 2)
                    continue;

                Vector3 point = start ? route.GetPoint(0) : route.GetPoint(route.PointCount - 1);
                bool duplicate = false;

                foreach (var existing in points)
                {
                    if ((existing - point).sqrMagnitude >= 0.01f)
                        continue;

                    duplicate = true;
                    break;
                }

                if (!duplicate)
                    points.Add(point);
            }

            points.Sort((a, b) => b.z.CompareTo(a.z));

            return points;
        }

        static string LabelOf(List<Vector3> points, Vector3 point, string[] labels)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if ((points[i] - point).sqrMagnitude >= 0.01f)
                    continue;

                if (i < labels.Length)
                    return labels[i];

                return (i + 1).ToString();
            }

            return "?";
        }

        /// <summary>이미 마커를 찍은 좌표면 건너뛴다. 시작 지점 2곳 / 종료 지점 2곳만 남는다.</summary>
        static void TryCreateSharedMarker(Transform parent, List<Vector3> used,
            string name, Vector3 position, Material material, float width)
        {
            foreach (var point in used)
            {
                if ((point - position).sqrMagnitude < 0.01f)
                    return;
            }

            used.Add(position);

            CreatePathMarker(parent, name, position, material, width);
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
        /// <summary>
        /// 기본 슬롯을 이름 기준으로 하나씩 보장한다. 씬에서 옮기거나 늘려 둔 슬롯은 그대로 둔다
        /// (경로와 어긋난 배치는 되돌리지 않고 씬 검증이 경고만 낸다).
        /// </summary>
        static void SetupSlots()
        {
            var slotRoot = GameObject.Find("Slots");

            if (slotRoot == null)
                slotRoot = new GameObject("Slots");

            var slotMat = EnsureMaterial("Mat_Slot", new Color(0.55f, 0.5f, 0.35f));
            var ringMat = EnsureFxMaterial("Mat_SlotRing", SelectionRingShader);
            var rangeMat = EnsureFxMaterial("Mat_Range", RangeSphereShader);

            for (int i = 0; i < DefaultSlotPositions.Length; i++)
            {
                string slotName = $"Slot_{i + 1:00}";
                var existing = slotRoot.transform.Find(slotName);

                if (existing != null)
                {
                    // 이미 있는 슬롯은 위치를 사용자가 옮겼을 수 있으므로 통째로 다시 만들지 않는다.
                    // 루트가 과거 버전(비균등 스케일 큐브)일 때만 교체하고, 그 외에는 표시 오브젝트만 보정한다
                    if (!IsLegacySlotRoot(existing))
                    {
                        EnsureSlotIndicators(existing, ringMat, rangeMat);
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }

                if (WarnIfPrefabInstance(slotRoot, slotName))
                    continue;

                CreateSlot(slotRoot.transform, slotName, DefaultSlotPositions[i], slotMat, ringMat, rangeMat);
            }
        }

        /// <summary>
        /// 씬 컨플릭트를 줄이려고 Slots/Paths 같은 골격을 프리팹으로 뽑아 쓸 수 있다.
        /// 그 상태에서 셋업이 자식을 새로 만들면 프리팹이 아니라 씬에 "추가된 오브젝트" 오버라이드가 쌓여
        /// 원본과 씬이 조용히 갈라진다. 그래서 만들지 않고 경고만 남긴다.
        /// </summary>
        static bool WarnIfPrefabInstance(GameObject root, string childName)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(root))
                return false;

            string path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root));

            Report($"{root.name}이 프리팹 인스턴스라 {childName}을 씬에 만들지 않았다. {path}를 열어 직접 추가할 것");

            return true;
        }

        /// <summary>
        /// 슬롯의 선택 링 / 사거리 표시 오브젝트만 현재 규격으로 다시 만든다.
        /// 셰이더가 바뀌어 메시 종류가 달라졌을 때(원판 -> 구, 큐브 -> 쿼드) 씬 전체 셋업 없이 갱신한다.
        /// </summary>
        public static void RefreshSlotIndicators()
        {
            var ringMat = EnsureFxMaterial("Mat_SlotRing", SelectionRingShader);
            var rangeMat = EnsureFxMaterial("Mat_Range", RangeSphereShader);

            var slots = UnityEngine.Object.FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);

            foreach (var slot in slots)
                EnsureSlotIndicators(slot.transform, ringMat, rangeMat);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Report($"슬롯 표시 오브젝트 {slots.Length}개 갱신");
        }

        /// <summary>루트가 과거 버전(납작한 큐브 자체가 슬롯)인지 확인한다.</summary>
        static bool IsLegacySlotRoot(Transform slot)
        {
            if (slot.GetComponent<MeshFilter>() != null)
                return true;

            if (slot.localScale != Vector3.one)
                return true;

            return false;
        }

        /// <summary>
        /// 선택 링(쿼드)과 사거리 표시(구)가 현재 규격인지 확인하고 아니면 그 자식만 다시 만든다.
        /// 슬롯 위치와 받침 비주얼은 건드리지 않는다.
        /// </summary>
        static void EnsureSlotIndicators(Transform slot, Material ringMat, Material rangeMat)
        {
            var ring = slot.Find("SelectionRing");

            if (!HasPrimitiveMesh(ring, "Quad"))
            {
                if (ring != null)
                    UnityEngine.Object.DestroyImmediate(ring.gameObject);

                CreateSelectionRing(slot, ringMat);
            }

            var range = slot.Find("RangeIndicator");

            if (!HasPrimitiveMesh(range, "Sphere"))
            {
                if (range != null)
                    UnityEngine.Object.DestroyImmediate(range.gameObject);

                CreateRangeIndicator(slot, rangeMat);
            }
        }

        /// <summary>자식이 특정 기본 도형 메시를 쓰고 있는지 확인한다.</summary>
        static bool HasPrimitiveMesh(Transform target, string meshName)
        {
            if (target == null)
                return false;

            var filter = target.GetComponent<MeshFilter>();

            if (filter == null || filter.sharedMesh == null)
                return false;

            return filter.sharedMesh.name.StartsWith(meshName, StringComparison.Ordinal);
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

            CreateSelectionRing(slot.transform, ringMat);
            CreateRangeIndicator(slot.transform, rangeMat);
        }

        /// <summary>선택 링: 바닥에 눕힌 Quad. 링/브래킷은 셰이더가 UV로 그리므로 스케일이 곧 지름이다.</summary>
        static void CreateSelectionRing(Transform slot, Material ringMat)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "SelectionRing";
            UnityEngine.Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(slot);
            ring.transform.localPosition = new Vector3(0f, 0.26f, 0f);
            ring.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ring.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            SetupFxRenderer(ring, ringMat);
            ring.SetActive(false);
        }

        /// <summary>
        /// 사거리 표시: 구 프록시. 셰이더가 씬 뎁스로 지형에 투영하므로 메시 자체는 보이지 않는다.
        /// 중심 높이를 BuildPosition(슬롯 + 0.25)에 맞춰야 실제 사거리 판정과 보이는 원이 일치한다.
        /// </summary>
        static void CreateRangeIndicator(Transform slot, Material rangeMat)
        {
            var range = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            range.name = "RangeIndicator";
            UnityEngine.Object.DestroyImmediate(range.GetComponent<Collider>());
            range.transform.SetParent(slot);
            range.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            range.transform.localRotation = Quaternion.identity;
            range.transform.localScale = Vector3.one;
            SetupFxRenderer(range, rangeMat);
            range.SetActive(false);
        }

        /// <summary>연출용 렌더러 공통 설정. 그림자와 라이트 프로브를 모두 끊는다.</summary>
        static void SetupFxRenderer(GameObject target, Material material)
        {
            var renderer = target.GetComponent<MeshRenderer>();

            if (renderer == null)
                return;

            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// 건설 실루엣(고스트)을 타워 종류별로 하나씩 베이크한다.
        /// 런타임에는 위치 이동과 활성화만 하므로 프리뷰 때문에 오브젝트를 만들지 않는다.
        /// 타워 프리팹 비주얼을 바꾼 뒤에는 이 액션을 다시 돌려야 실루엣에 반영된다.
        /// </summary>
        public static BuildGhostPreview BakeBuildGhosts()
        {
            var root = GameObject.Find("BuildGhosts");

            if (root == null)
                root = new GameObject("BuildGhosts");

            var preview = EnsureComponent<BuildGhostPreview>(root);
            var ghostMat = EnsureFxMaterial("Mat_BuildGhost", BuildGhostShader);

            if (ghostMat == null)
                return preview;

            string[] towerAssets = { "Tower_Infantry", "Tower_Archer", "Tower_Mage", "Tower_Artillery" };
            var types = new List<TowerType>();
            var objects = new List<GameObject>();

            foreach (string assetName in towerAssets)
            {
                var data = AssetDatabase.LoadAssetAtPath<TowerData>($"{DataTowers}/{assetName}.asset");

                if (data == null || data.TowerPrefab == null)
                    continue;

                var ghost = CreateGhostObject(root.transform, $"Ghost_{data.Type}", data.TowerPrefab, ghostMat);

                if (ghost == null)
                    continue;

                types.Add(data.Type);
                objects.Add(ghost);
            }

            var so = new SerializedObject(preview);
            var typesProp = so.FindProperty("_ghostTypes");
            var objectsProp = so.FindProperty("_ghostObjects");

            typesProp.arraySize = types.Count;
            objectsProp.arraySize = objects.Count;

            for (int i = 0; i < types.Count; i++)
            {
                typesProp.GetArrayElementAtIndex(i).enumValueIndex = (int)types[i];
                objectsProp.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            // 씬 셋업 전체를 돌리지 않고 재베이크만 했을 때도 UI가 프리뷰를 찾을 수 있게 연결한다
            LinkGhostPreviewToBuildMenu(preview);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Report($"건설 실루엣 {objects.Count}종 베이크");

            return preview;
        }

        /// <summary>씬의 BuildMenu가 고스트 프리뷰를 참조하도록 비어 있을 때만 채운다.</summary>
        static void LinkGhostPreviewToBuildMenu(BuildGhostPreview preview)
        {
            var menu = UnityEngine.Object.FindFirstObjectByType<BuildMenu>();

            if (menu == null)
                return;

            var so = new SerializedObject(menu);
            FillIfEmpty(so, "_ghostPreview", preview);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>타워 프리팹을 복제해 형태만 남긴 고스트를 만든다. 프리팹 변경을 반영하려고 매번 새로 만든다.</summary>
        static GameObject CreateGhostObject(Transform parent, string name, GameObject towerPrefab, Material ghostMat)
        {
            var existing = parent.Find(name);

            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(towerPrefab, parent);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // 로직 컴포넌트가 남은 고스트는 활성화되는 순간 실제로 동작해 버린다.
            // 그런 고스트는 쓰지 않고 버린다 (실루엣 하나를 포기하는 편이 안전하다)
            if (!StripToGhostVisual(instance, ghostMat))
            {
                Report($"{name}: 비주얼만 남기지 못해 실루엣 생성을 취소함");
                UnityEngine.Object.DestroyImmediate(instance);

                return null;
            }

            instance.SetActive(false);

            return instance;
        }

        /// <summary>
        /// 고스트는 형태만 필요하다. 렌더링 컴포넌트만 남기고 나머지는 지운 뒤 머티리얼을 실루엣으로 바꾼다.
        /// 로직 컴포넌트가 하나라도 남으면 false를 돌려준다.
        /// </summary>
        static bool StripToGhostVisual(GameObject root, Material ghostMat)
        {
            var components = root.GetComponentsInChildren<Component>(true);

            // 컴포넌트 간 의존(RequireComponent) 때문에 뒤에서부터 지운다
            for (int i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];

                if (component == null)
                    continue;

                if (IsGhostVisualComponent(component))
                    continue;

                try
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
                catch (Exception)
                {
                    // 남은 것은 아래 재검사에서 잡힌다
                }
            }

            // DestroyImmediate가 예외 없이 거부하는 경우도 있으므로 결과를 직접 다시 확인한다
            var remaining = root.GetComponentsInChildren<Component>(true);

            foreach (var component in remaining)
            {
                if (component == null)
                    continue;

                if (IsGhostVisualComponent(component))
                    continue;

                Report($"{root.name}: 로직 컴포넌트 {component.GetType().Name}가 남음");

                return false;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                var materials = new Material[renderer.sharedMaterials.Length];

                for (int i = 0; i < materials.Length; i++)
                    materials[i] = ghostMat;

                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            return true;
        }

        /// <summary>고스트에 남겨도 되는(형태 렌더링에만 관여하는) 컴포넌트인지 확인한다.</summary>
        static bool IsGhostVisualComponent(Component component)
        {
            if (component is Transform)
                return true;

            if (component is MeshFilter)
                return true;

            if (component is MeshRenderer || component is SkinnedMeshRenderer)
                return true;

            return false;
        }

        /// <summary>
        /// 게임플레이 연출용 커스텀 셰이더 머티리얼.
        /// 에셋이 이미 있으면 사용자가 조정한 값을 살리기 위해 그대로 쓴다.
        /// </summary>
        static Material EnsureFxMaterial(string name, string shaderName)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
                return existing;

            var shader = Shader.Find(shaderName);

            if (shader == null)
            {
                Report($"셰이더를 찾지 못함: {shaderName} ({name} 생성 건너뜀)");
                return null;
            }

            var mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);

            return mat;
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

        static StageController SetupStageController(PathRoute[] paths)
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
            FillArrayIfEmpty(so, "_paths", paths);
            FillIfEmpty(so, "_spawner", spawner);
            FillIfEmpty(so, "_rewards", rewards);
            so.ApplyModifiedPropertiesWithoutUndo();

            var rewardSo = new SerializedObject(rewards);
            FillIfEmpty(rewardSo, "_stage", stage);
            FillIfEmpty(rewardSo, "_config", rewardConfig);
            FillIfEmpty(rewardSo, "_luckFx", LoadLuckSparkPrefab());
            rewardSo.ApplyModifiedPropertiesWithoutUndo();

            var spawnerSo = new SerializedObject(spawner);
            FillIfEmpty(spawnerSo, "_spawnFx", LoadSmokePuffPrefab());
            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

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

        /// <summary>
        /// 배열 필드를 채운다. 비어 있거나 항목에 빈 참조가 있으면 셋업 값으로 다시 만든다.
        /// 사용자가 순서를 바꿔 두기만 한 경우(길이가 같고 전부 채워져 있음)는 건드리지 않는다.
        /// </summary>
        static void FillArrayIfEmpty(SerializedObject so, string propertyPath, UnityEngine.Object[] values)
        {
            var property = so.FindProperty(propertyPath);

            if (property == null)
            {
                Report($"필드 {propertyPath}를 찾을 수 없음 - 스크립트와 셋업 코드가 어긋남");
                return;
            }

            if (!property.isArray)
            {
                Report($"필드 {propertyPath}가 배열이 아님");
                return;
            }

            bool needsFill = property.arraySize != values.Length;

            for (int i = 0; !needsFill && i < property.arraySize; i++)
            {
                if (property.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    needsFill = true;
            }

            if (!needsFill)
                return;

            property.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static void SetupGameUI(StageController stage, BuildGhostPreview ghostPreview)
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
            var rewardSidebar = EnsureComponent<RewardSidebar>(uiGo);
            EnsureComponent<MonsterHealthOverlay>(uiGo);

            var debugView = EnsureComponent<MonsterDebugView>(uiGo);

            var debugSo = new SerializedObject(debugView);
            var bands = debugSo.FindProperty("_bandMaterials");

            if (bands != null && bands.arraySize == 0)
            {
                var materials = EnsureDebugHpMaterials();
                bands.arraySize = materials.Length;

                for (int i = 0; i < materials.Length; i++)
                    bands.GetArrayElementAtIndex(i).objectReferenceValue = materials[i];
            }

            debugSo.ApplyModifiedPropertiesWithoutUndo();

            var hudSo = new SerializedObject(hud);
            FillIfEmpty(hudSo, "_stage", stage);
            FillIfEmpty(hudSo, "_debugView", debugView);
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            var overlaySo = new SerializedObject(rewardOverlay);
            FillIfEmpty(overlaySo, "_stage", stage);
            FillIfEmpty(overlaySo, "_rewards", stage.GetComponent<RewardSystem>());
            overlaySo.ApplyModifiedPropertiesWithoutUndo();

            var sidebarSo = new SerializedObject(rewardSidebar);
            FillIfEmpty(sidebarSo, "_stage", stage);
            FillIfEmpty(sidebarSo, "_rewards", stage.GetComponent<RewardSystem>());
            sidebarSo.ApplyModifiedPropertiesWithoutUndo();

            var dashSo = new SerializedObject(dashboard);
            FillIfEmpty(dashSo, "_stage", stage);
            dashSo.ApplyModifiedPropertiesWithoutUndo();

            var menuSo = new SerializedObject(buildMenu);
            FillIfEmpty(menuSo, "_stage", stage);
            FillIfEmpty(menuSo, "_ghostPreview", ghostPreview);
            FillIfEmpty(menuSo, "_buildFx", LoadSmokePuffPrefab());

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

                if (data.Levels == null || data.Levels.Length != 3)
                    issues.Add($"[데이터] {name}: 레벨이 3단계가 아님 (최종 증축은 분기)");

                if (data.BranchA == null || !data.BranchA.IsValid)
                    issues.Add($"[데이터] {name}: BranchA가 비어 있음");

                if (data.BranchB == null || !data.BranchB.IsValid)
                    issues.Add($"[데이터] {name}: BranchB가 비어 있음");

                ValidateBranchSkills(issues, name, data.BranchA);
                ValidateBranchSkills(issues, name, data.BranchB);

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

        static void ValidateBranchSkills(List<string> issues, string towerName, TowerBranchDef branch)
        {
            if (branch == null || branch.Skills == null)
                return;

            foreach (var skill in branch.Skills)
            {
                if (skill == null)
                {
                    issues.Add($"[데이터] {towerName}/{branch.Name}: 빈 스킬 항목");
                    continue;
                }

                if (skill.Type == BranchSkillType.None)
                    issues.Add($"[데이터] {towerName}/{skill.DisplayName}: 효과 미지정");

                if (skill.TotalCost <= 0)
                    issues.Add($"[데이터] {towerName}/{skill.DisplayName}: 총액이 0 이하");

                if (skill.Values == null || skill.Values.Length != 3)
                    issues.Add($"[데이터] {towerName}/{skill.DisplayName}: 레벨 수치가 3개가 아님");
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

            if (stage.Waves == null || stage.Waves.Length != 24)
            {
                issues.Add("[데이터] Stage01: 웨이브가 24개가 아님");
                return;
            }

            if (stage.RandomPool == null || stage.RandomPool.Length == 0)
                issues.Add("[데이터] Stage01: RandomPool이 비어 있음 (무작위 구성 불가)");
            else
            {
                foreach (var monster in stage.RandomPool)
                {
                    if (monster == null)
                        issues.Add("[데이터] Stage01: RandomPool에 빈 항목");
                }
            }

            for (int i = 0; i < stage.Waves.Length; i++)
            {
                var wave = stage.Waves[i];

                if (wave == null)
                {
                    issues.Add($"[데이터] Stage01: 웨이브 {i + 1}이 null");
                    continue;
                }

                if (wave.Budget <= 0)
                    issues.Add($"[데이터] Stage01: 웨이브 {i + 1} 예산이 0 이하");

                if (wave.StatMultiplier <= 0f)
                    issues.Add($"[데이터] Stage01: 웨이브 {i + 1} 스탯 배수가 0 이하");

                bool hasBoss = false;
                bool hasFinalBoss = false;

                if (wave.Entries != null)
                {
                    foreach (var entry in wave.Entries)
                    {
                        if (entry.Monster == null)
                        {
                            issues.Add($"[데이터] Stage01: 웨이브 {i + 1}에 몬스터 미지정 항목");
                            continue;
                        }

                        if (entry.Monster.IsBoss)
                            hasBoss = true;

                        if (entry.Monster.IsFinalBoss)
                            hasFinalBoss = true;
                    }
                }

                if (wave.IsBossWave && !hasBoss)
                    issues.Add($"[데이터] Stage01: 웨이브 {i + 1}이 보스 웨이브인데 보스 항목이 없음");

                if (wave.IsFinalWave && !hasFinalBoss)
                    issues.Add($"[데이터] Stage01: 웨이브 {i + 1}이 최종 보스 웨이브인데 최종 보스 항목이 없음");
            }

            ValidateArchetypes(stage, issues);
        }

        /// <summary>
        /// 아키타입 표 검증. 어느 구간에서든 뽑을 후보가 0이면 그 웨이브의 스폰이 조용히 비어버린다.
        /// 연속 금지 규칙이 있으므로 구간마다 확률이 있는 아키타입이 2종 이상 필요하다.
        /// </summary>
        /// <summary>중간 보스 직후 웨이브. 아키타입 후보가 가장 좁아지는 지점이다.</summary>
        static readonly int[] AfterBossWaves = { 7, 13, 19 };

        static void ValidateArchetypes(StageData stage, List<string> issues)
        {
            if (stage.Archetypes == null || stage.Archetypes.Length == 0)
            {
                issues.Add("[데이터] Stage01: 아키타입이 비어 있음 (7웨이브 이후 스폰 불가)");
                return;
            }

            bool hasRandomPoolArchetype = false;

            for (int band = 0; band < 3; band++)
            {
                int candidates = 0;

                foreach (var archetype in stage.Archetypes)
                {
                    if (archetype == null)
                        continue;

                    if (archetype.BandChance == null || band >= archetype.BandChance.Length)
                        continue;

                    if (archetype.BandChance[band] > 0f)
                        candidates++;
                }

                if (candidates < 2)
                    issues.Add($"[데이터] Stage01: 아키타입 등장 확률 구간 {band + 1}의 후보가 {candidates}종 (연속 금지 규칙 때문에 2종 이상 필요)");
            }

            // 보스 직후 웨이브(7/13/19)는 AllowedAfterBoss와 MinWave까지 걸려 후보가 크게 줄어든다.
            // 여기서 0이 되면 런타임이 연속 금지를 풀고 뽑거나 무작위 풀로 폴백한다.
            foreach (int waveNumber in AfterBossWaves)
            {
                int band = waveNumber <= 12 ? 0 : waveNumber <= 18 ? 1 : 2;
                int candidates = 0;

                foreach (var archetype in stage.Archetypes)
                {
                    if (WaveSpawner.IsArchetypeAllowed(archetype, waveNumber, band, true))
                        candidates++;
                }

                if (candidates < 2)
                    issues.Add($"[데이터] Stage01: {waveNumber}웨이브(보스 직후) 아키타입 후보가 {candidates}종 (연속 금지를 풀어야 뽑힌다)");
            }

            foreach (var archetype in stage.Archetypes)
            {
                if (archetype == null)
                {
                    issues.Add("[데이터] Stage01: 아키타입 목록에 빈 항목");
                    continue;
                }

                if (archetype.UsesRandomPool)
                {
                    hasRandomPoolArchetype = true;
                    continue;
                }

                float shareSum = 0f;

                foreach (var member in archetype.Members)
                {
                    if (member == null || member.Monster == null)
                    {
                        issues.Add($"[데이터] Stage01: 아키타입 {archetype.Name}에 몬스터 미지정 구성원");
                        continue;
                    }

                    shareSum += member.Share;
                }

                if (Mathf.Abs(shareSum - 1f) > 0.01f)
                    issues.Add($"[데이터] Stage01: 아키타입 {archetype.Name}의 예산 배분 합이 {shareSum:F2} (1이어야 함)");
            }

            // 짬 아키타입은 보스 직후 웨이브와 24웨이브 추가 스폰의 최후 후보다
            if (!hasRandomPoolArchetype)
                issues.Add("[데이터] Stage01: 무작위 풀을 쓰는 아키타입(짬)이 없음 - 최종 보스전 추가 스폰이 비게 된다");

            if (stage.BossArchetypes == null || stage.BossArchetypes.Length == 0)
                issues.Add("[데이터] Stage01: 중간 보스 종류가 비어 있음 (슬롯 기본 스탯으로 등장)");
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

            float targetSum = config.TargetCommon + config.TargetRare + config.TargetHeroic + config.TargetLegendary;

            if (targetSum <= 0f)
                issues.Add("[보상] 등급 목표 확률 합이 0");
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
            string[] fields = { "_stageData", "_difficulty", "_spawner", "_rewards" };

            foreach (var field in fields)
            {
                var property = so.FindProperty(field);

                if (property == null)
                {
                    issues.Add($"[씬] StageController.{field} 필드를 찾을 수 없음 - 스크립트와 셋업 코드가 어긋남");
                    continue;
                }

                if (property.objectReferenceValue == null)
                    issues.Add($"[씬] StageController.{field} 참조 비어 있음");
            }

            var pathsProperty = so.FindProperty("_paths");

            if (pathsProperty == null || !pathsProperty.isArray)
            {
                issues.Add("[씬] StageController._paths 필드를 찾을 수 없음 - 스크립트와 셋업 코드가 어긋남");
            }
            else if (pathsProperty.arraySize != DefaultRoutes.Length)
            {
                issues.Add($"[씬] StageController._paths가 {pathsProperty.arraySize}개 - {DefaultRoutes.Length}개 필요 (씬 셋업 실행)");
            }
            else
            {
                for (int i = 0; i < pathsProperty.arraySize; i++)
                {
                    if (pathsProperty.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        issues.Add($"[씬] StageController._paths[{i}] 참조 비어 있음");
                }
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

            var routes = UnityEngine.Object.FindObjectsByType<PathRoute>(FindObjectsSortMode.None);

            if (routes.Length != DefaultRoutes.Length)
                issues.Add($"[씬] 경로 루트가 {routes.Length}개 - {DefaultRoutes.Length}개(A1/A2/B1/B2) 필요");

            foreach (var route in routes)
            {
                if (route.PointCount < 2)
                    issues.Add($"[씬] 루트 {route.RouteId}: 웨이포인트가 2개 미만");
            }

            if (GameObject.Find("PathVisual") == null)
                issues.Add("[씬] 경로 비주얼(PathVisual)이 없음 - 경로 베이크 실행 필요");

            var slots = UnityEngine.Object.FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);

            if (slots.Length == 0)
                issues.Add("[씬] TowerSlot이 하나도 없음");

            foreach (var slot in slots)
            {
                // 슬롯이 길 위에 올라타면 건설 클릭과 경로 비주얼이 겹친다
                Vector3 origin = slot.transform.position;
                origin.y = 0f;

                foreach (var route in routes)
                {
                    if (route.PointCount < 2)
                        continue;

                    route.ClosestPoint(origin, out float sqrDistance);

                    if (sqrDistance >= SlotPathClearance * SlotPathClearance)
                        continue;

                    issues.Add($"[씬] {slot.name}: 루트 {route.RouteId}와 {Mathf.Sqrt(sqrDistance):F2} 거리 - {SlotPathClearance} 이상 필요");
                }

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
