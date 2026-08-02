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

        [Header("보병 전용")]
        public int SoldierCount;
        public float SoldierHp;
        public float SoldierDamage;
        public float SoldierAttackInterval;
        public float SoldierRespawnSeconds;
    }

    /// <summary>타워 계열 1개의 정의 (Lv1~4 선형 강화). 기획서(코어 룰) 2장.</summary>
    [CreateAssetMenu(menuName = "Rush/Tower Data", fileName = "TowerData")]
    public class TowerData : ScriptableObject
    {
        public TowerType Type;
        public DamageType DamageType = DamageType.Physical;
        public GameObject TowerPrefab;
        public GameObject ProjectilePrefab;
        public GameObject SoldierPrefab;
        public float ProjectileSpeed = 12f;
        public TowerLevelStat[] Levels = new TowerLevelStat[4];

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
