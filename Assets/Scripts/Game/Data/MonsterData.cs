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

        [Header("보스 여부 (처치 시 보상 제시, 통과 시 라이프 20 차감)")]
        public bool IsBoss;

        [Tooltip("최종 보스 (24웨이브). 처치하면 잡졸이 남아 있어도 즉시 승리")]
        public bool IsFinalBoss;

        [Header("배치 스폰 (시트: 배치 크기 - 스폰 단위)")]
        [Tooltip("한 배치에 묶여 나오는 마릿수")]
        public int BatchSize = 1;

        [Tooltip("간격이 하한보다 좁을 때 배치 크기를 2배로 늘릴 수 있는지 (마법사/백인대장은 제외)")]
        public bool AllowBatchGrowth = true;

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
