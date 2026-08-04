using UnityEngine;

namespace Rush.Data
{
    /// <summary>
    /// 로그라이트 보상 카드 1장의 정의. 스프레드시트 한 행에 대응한다 (Id = 시트 ID).
    /// 수치(Value/Value2/Chance/Duration/StackLimit)는 전부 여기 있고 Balance Board에서 조절한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Rush/Reward Definition", fileName = "Reward")]
    public class RewardDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public RewardCategory Category;
        public RewardRarity Rarity;

        [TextArea] public string Description;
        [TextArea] public string ConditionNote;

        [Header("적용 범위")]
        public RewardTowerFilter TowerFilter = RewardTowerFilter.Any;
        public DamageTag Tag = DamageTag.None;

        [Header("효과")]
        public RewardEffectType Effect = RewardEffectType.None;
        public float Value;
        public float Value2;
        [Range(0f, 1f)] public float Chance;
        public float Duration;

        [Header("운영")]
        [Min(1)] public int StackLimit = 1;
        public bool Enabled = true;
        public string DisabledReason;

        /// <summary>필터가 이 타워 종류에 적용되는지.</summary>
        public bool AppliesTo(TowerType type)
        {
            if (TowerFilter == RewardTowerFilter.Any)
                return true;

            if (TowerFilter == RewardTowerFilter.Archer && type == TowerType.Archer)
                return true;

            if (TowerFilter == RewardTowerFilter.Mage && type == TowerType.Mage)
                return true;

            if (TowerFilter == RewardTowerFilter.Artillery && type == TowerType.Artillery)
                return true;

            if (TowerFilter == RewardTowerFilter.Infantry && type == TowerType.Infantry)
                return true;

            return false;
        }
    }
}
