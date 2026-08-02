using System;
using UnityEngine;

namespace Rush.Data
{
    /// <summary>
    /// 기본 공격 위에 얹는 추가 발사 규칙. 지금은 기본 켜짐으로 두고 플레이 감각을 본다.
    /// 어떤 상위 시스템(아이템/특성 등)이 이 값을 제어할지는 아직 정하지 않았다.
    ///
    /// - 확률 발사: 공격할 때마다 확률로 추가 발사체가 나간다 (Risk of Rain 미사일류).
    /// - 처치 시 발사: 이 타워가 적을 죽이면 주변 적을 향해 여러 발이 튄다 (Ceremonial Dagger류).
    ///
    /// 추가 발사체는 다시 추가 발사를 유발하지 않는다 (연쇄 폭주 방지).
    /// </summary>
    [Serializable]
    public class AttackExtras
    {
        [Header("확률 발사")]
        public bool ProcEnabled = true;
        [Range(0f, 1f)] public float ProcChance = 0.15f;
        public int ProcCount = 1;
        public float ProcDamageScale = 1.5f;
        public float ProcSplashRadius = 1f;
        public float ProcSpeed = 9f;
        public GameObject ProcPrefab;
        public GameObject ProcImpactPrefab;
        public ProjectileMotion ProcMotion = new ProjectileMotion();

        [Header("처치 시 발사")]
        public bool OnKillEnabled = true;
        public int OnKillCount = 3;
        public float OnKillDamageScale = 0.6f;
        public float OnKillSearchRadius = 5f;
        public float OnKillSpeed = 11f;
        public GameObject OnKillPrefab;
        public GameObject OnKillImpactPrefab;
        public ProjectileMotion OnKillMotion = new ProjectileMotion();

        public bool AnyEnabled
        {
            get
            {
                if (ProcEnabled)
                    return true;

                if (OnKillEnabled)
                    return true;

                return false;
            }
        }
    }
}
