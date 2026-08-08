using System;
using UnityEngine;

namespace Rush.Data
{
    /// <summary>
    /// 기본 공격 위에 얹는 추가 발사 규칙. 발동 여부/수치는 로그라이트 보상이 제어한다.
    ///
    /// - 확률 발사: 공격할 때마다 확률로 추가 발사체가 나간다 (보상 C13 연발 장전).
    /// - 처치 시 발사: 이 타워가 적을 죽이면 주변 적을 향해 여러 발이 튄다 (보상 C14 처형 예포).
    ///
    /// 여기 있는 값은 연출 파라미터(프리팹/궤적/속도/탐색 반경)가 본체이고,
    /// Enabled/Chance/Count/DamageScale은 보상 없이 연출을 확인하기 위한 개발자 강제 켜기다.
    /// 그래서 기본은 꺼짐이며, 보상을 뽑으면 꺼져 있어도 발동한다.
    ///
    /// 추가 발사체는 다시 추가 발사를 유발하지 않는다 (연쇄 폭주 방지).
    /// </summary>
    [Serializable]
    public class AttackExtras
    {
        [Header("확률 발사 (개발자 강제 켜기)")]
        public bool ProcEnabled = false;
        [Range(0f, 1f)] public float ProcChance = 0.15f;
        public int ProcCount = 1;
        public float ProcDamageScale = 1.5f;
        public float ProcSplashRadius = 1f;
        public float ProcSpeed = 9f;
        public GameObject ProcPrefab;
        public GameObject ProcImpactPrefab;
        public ProjectileMotion ProcMotion = new ProjectileMotion();

        [Header("처치 시 발사 (개발자 강제 켜기)")]
        public bool OnKillEnabled = false;
        public int OnKillCount = 3;
        public float OnKillDamageScale = 0.6f;
        public float OnKillSearchRadius = 5f;
        public float OnKillSpeed = 11f;
        public GameObject OnKillPrefab;
        public GameObject OnKillImpactPrefab;
        public ProjectileMotion OnKillMotion = new ProjectileMotion();
    }
}
