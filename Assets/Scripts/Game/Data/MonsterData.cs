using UnityEngine;

namespace Rush.Data
{
    /// <summary>몬스터 1종의 정의. 기획서(코어 룰) 3장, 몬스터 유닛 리스트 기획서.</summary>
    [CreateAssetMenu(menuName = "Rush/Monster Data", fileName = "MonsterData")]
    public class MonsterData : ScriptableObject
    {
        public string DisplayName;
        public GameObject Prefab;

        [Header("기본 스탯")]
        public float MaxHp = 60f;
        public float MoveSpeed = 1.5f;
        public DefenseGrade PhysicalDefense = DefenseGrade.Low;
        public DefenseGrade MagicalDefense = DefenseGrade.Low;
        public bool IsFlying;

        [Header("보상 / 페널티")]
        public int GoldReward = 5;
        public int LifeDamage = 1;

        [Header("마법 특성 (마법 유닛 전용)")]
        public float RegenPerSecond;

        [Header("근접 반격 (병사에게 저지당했을 때)")]
        public float MeleeDamage = 5f;
        public float MeleeInterval = 1f;

        [Header("원거리 공격 (몬스터 궁병 전용, 0이면 없음)")]
        public float RangedDamage;
        public float RangedRange;
        public float RangedInterval = 1.5f;
    }
}
