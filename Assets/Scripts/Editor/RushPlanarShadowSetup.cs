using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Common.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rush.EditorTools
{
    /// <summary>
    /// 몬스터 프리팹의 캐스트 섀도우를 끈다. 플래너 섀도우(PlanarShadowRendererFeature)가
    /// 접지 그림자를 대신하므로 라이트 섀도우 맵에는 넣지 않는다.
    ///
    /// 런타임에 세팅하지 않고 에디트 타임에 프리팹에 굽는다. 아트 모델을 다시 주입한 뒤
    /// (Rush > 전체 셋업 등) 자동으로 한 번 더 돌아간다. 멱등이라 여러 번 실행해도 안전하다.
    ///
    /// 그림자를 그릴 대상 선별은 레이어(Enemy)로 하며, 레이어 지정은 프리팹에서 직접 한다.
    /// 렌더러가 붙은 오브젝트의 레이어를 보므로 모델 자식까지 함께 바꿔야 한다.
    /// 이 액션은 레이어가 아직 안 잡힌 프리팹을 로그로 알려준다.
    /// </summary>
    public static class RushPlanarShadowSetup
    {
        const string PrefabDir = "Assets/RushGame/Prefabs";
        const string MonsterPrefix = "Monster_";

        [MenuItem("Rush/적 캐스트 섀도우 끄기 (플래너 섀도우용)", false, 320)]
        public static void Run()
        {
            int enemyLayer = LayerMask.NameToLayer(PlanarShadowRendererFeature.EnemyLayerName);

            var prefabPaths = CollectMonsterPrefabPaths();

            if (prefabPaths.Count == 0)
            {
                Debug.LogWarning($"[PlanarShadow] {PrefabDir} 에서 몬스터 프리팹을 찾지 못했다.");
                return;
            }

            int changedPrefabs = 0;
            int changedRenderers = 0;
            var missingLayer = new List<string>();

            foreach (string prefabPath in prefabPaths)
            {
                var result = ProcessPrefab(prefabPath, enemyLayer);

                if (!result.HasEnemyLayer)
                    missingLayer.Add(Path.GetFileNameWithoutExtension(prefabPath));

                if (result.ChangedRenderers <= 0)
                    continue;

                changedPrefabs++;
                changedRenderers += result.ChangedRenderers;
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[PlanarShadow] 몬스터 프리팹 {prefabPaths.Count}종 확인 / 갱신 {changedPrefabs}종 / 캐스트 섀도우 Off {changedRenderers}개");

            if (missingLayer.Count > 0)
                Debug.LogWarning($"[PlanarShadow] 렌더러 레이어가 '{PlanarShadowRendererFeature.EnemyLayerName}'가 아닌 프리팹 {missingLayer.Count}종 - 그림자가 안 나온다: {string.Join(", ", missingLayer)}");
        }

        static List<string> CollectMonsterPrefabPaths()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileName(path).StartsWith(MonsterPrefix, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>프리팹 하나를 열어 캐스트 섀도우를 끄고, 레이어 상태를 같이 확인한다.</summary>
        static PrefabResult ProcessPrefab(string prefabPath, int enemyLayer)
        {
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);

            // 중간에 예외가 나도 격리 씬이 남지 않게 언로드는 finally에서만 한다
            try
            {
                var renderers = contents.GetComponentsInChildren<Renderer>(true);

                int changed = 0;
                int targets = 0;
                int onEnemyLayer = 0;

                foreach (var renderer in renderers)
                {
                    if (!IsShadowTarget(renderer))
                        continue;

                    targets++;

                    if (renderer.gameObject.layer == enemyLayer)
                        onEnemyLayer++;

                    if (renderer.shadowCastingMode == ShadowCastingMode.Off)
                        continue;

                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    changed++;
                }

                if (changed > 0)
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);

                // 대상 렌더러가 하나도 없으면 그림자도 안 나오므로 미설정으로 본다
                return new PrefabResult
                {
                    ChangedRenderers = changed,
                    HasEnemyLayer = targets > 0 && onEnemyLayer == targets,
                };
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>그림자를 그릴 대상 렌더러인지. 꺼둔 더미 큐브나 파티클류는 제외한다.</summary>
        static bool IsShadowTarget(Renderer renderer)
        {
            if (renderer == null)
                return false;

            if (!renderer.enabled)
                return false;

            if (renderer is SkinnedMeshRenderer)
                return true;

            if (renderer is MeshRenderer)
                return true;

            return false;
        }

        struct PrefabResult
        {
            public int ChangedRenderers;

            /// <summary>대상 렌더러 전부가 Enemy 레이어인지.</summary>
            public bool HasEnemyLayer;
        }
    }
}
