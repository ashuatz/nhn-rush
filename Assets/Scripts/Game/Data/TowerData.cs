using System;
using UnityEngine;

namespace Rush.Data
{
    /// <summary>타워 한 레벨의 스탯. 계열에서 안 쓰는 필드는 0으로 둔다.</summary>
    [Serializable]
    public class TowerLevelStat
    {
        public string DisplayName;
        public int Cost;
        public float Damage;
        public float Range;
        public float AttackInterval;

        [Header("마도 전용")]
        [Range(0f, 1f)] public float SlowPercent;
        public float SlowDuration;

        [Header("포병 전용")]
        public float SplashRadius;
        [Range(0f, 1f)] public float ArmorPierce;

        [Tooltip("폭발 가장자리에서 받는 피해 비율. 중심 1.0에서 이 값까지 선형 감쇠한다 (1 = 감쇠 없음)")]
        [Range(0f, 1f)] public float SplashEdgeDamage = 1f;

        [Header("보병 전용 (공격력은 최소~최대 범위)")]
        public int SoldierCount;
        public float SoldierHp;
        public float SoldierDamage;
        public float SoldierDamageMax;
        public float SoldierAttackInterval;
        public float SoldierRespawnSeconds;

        [Tooltip("교전이 끊긴 병사가 초당 회복하는 체력. 0이면 회복하지 않는다")]
        public float SoldierRegenPerSecond;

        [Tooltip("병사 피해 감소율 (기사단 방어/저항 1단계 = 0.25 근사)")]
        [Range(0f, 1f)] public float SoldierDamageCut;
    }

    /// <summary>
    /// 타워 계열 1개의 정의. 1~3단계(Levels) 뒤 최종 분기(BranchA/B) 중 하나를 선택한다 (되돌릴 수 없음).
    /// 스프레드시트(타워 시트/막증축 타워 스킬 정리).
    /// </summary>
    [CreateAssetMenu(menuName = "Rush/Tower Data", fileName = "TowerData")]
    public class TowerData : ScriptableObject
    {
        public TowerType Type;
        public DamageType DamageType = DamageType.Physical;
        public GameObject TowerPrefab;
        public GameObject ProjectilePrefab;
        public GameObject SoldierPrefab;
        public GameObject ImpactPrefab;
        public float ProjectileSpeed = 12f;

        [Tooltip("1~3단계 스탯. 최종 증축은 BranchA/B에 있다")]
        public TowerLevelStat[] Levels = new TowerLevelStat[3];

        [Header("최종 분기 (4단계)")]
        public TowerBranchDef BranchA = new TowerBranchDef();
        public TowerBranchDef BranchB = new TowerBranchDef();

        [Header("공격 연출")]
        public ProjectileMotion Motion = new ProjectileMotion();

        [Header("추가 발사 규칙 (개발자 실험용, 기본 꺼짐)")]
        public AttackExtras Extras = new AttackExtras();

        /// <summary>공중 유닛 공격 가능 여부. 근접(보병)/포병은 불가. 기획서(코어 룰) 3.3.</summary>
        public bool CanTargetFlying
        {
            get
            {
                if (Type == TowerType.Archer)
                    return true;

                if (Type == TowerType.Mage)
                    return true;

                return false;
            }
        }
    }
}
