using Rush.Combat;
using Rush.Data;
using Rush.Stage;
using UnityEditor;
using UnityEngine;

namespace Rush.EditorTools
{
    /// <summary>
    /// 개발용 치트. 손으로 짓고 강화하는 과정을 건너뛰고 최종 단계 상태를 바로 만든다.
    /// 골드는 차감하지 않으며 플레이 모드에서만 동작한다.
    /// </summary>
    public static class RushCheats
    {
        const string ArcherDataPath = "Assets/RushGame/Data/Towers/Tower_Archer.asset";

        [MenuItem("Rush/치트/빈 슬롯 전부 궁수타워 L4", false, 400)]
        public static void BuildAllArcherLv4()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[치트] 플레이 모드에서만 쓸 수 있다.");
                return;
            }

            var stage = Object.FindFirstObjectByType<StageController>();

            if (stage == null)
            {
                Debug.LogWarning("[치트] 씬에 StageController가 없다.");
                return;
            }

            var data = AssetDatabase.LoadAssetAtPath<TowerData>(ArcherDataPath);

            if (data == null || data.TowerPrefab == null)
            {
                Debug.LogWarning($"[치트] 궁수 타워 데이터/프리팹을 못 찾았다 ({ArcherDataPath}).");
                return;
            }

            var slots = Object.FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);
            int built = 0;

            foreach (var slot in slots)
            {
                if (slot.IsOccupied)
                    continue;

                // 분기는 A/B를 번갈아 골라 두 모델을 한 판에서 같이 본다
                var choice = TowerBranchChoice.B;

                if (built % 2 == 0)
                    choice = TowerBranchChoice.A;

                if (!BuildMaxTower(slot, data, stage, choice))
                    continue;

                built++;
            }

            GameLog.Info("Cheat", $"궁수타워 L4 {built}개 건설 (슬롯 {slots.Length}개 중)");
        }

        /// <summary>슬롯에 타워를 세우고 3단계까지 무료로 올린 뒤 최종 분기를 확정한다.</summary>
        static bool BuildMaxTower(TowerSlot slot, TowerData data, StageController stage, TowerBranchChoice choice)
        {
            var go = Object.Instantiate(data.TowerPrefab, slot.BuildPosition, Quaternion.identity, slot.transform);
            var tower = go.GetComponent<Tower>();

            if (tower == null)
            {
                Debug.LogWarning($"[치트] {data.name}: 프리팹에 Tower 컴포넌트가 없다.");
                Object.Destroy(go);

                return false;
            }

            tower.Initialize(data, stage);
            slot.Occupant = tower;

            while (tower.CanUpgrade)
                tower.Upgrade();

            if (tower.CanChooseBranch)
                tower.ChooseBranch(choice);

            return true;
        }
    }
}
