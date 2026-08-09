using System;
using UnityEngine;

namespace Rush.Data
{
    /// <summary>
    /// 최종 분기 전용 스킬 효과 종류. 스프레드시트(막증축 타워 스킬 정리) 18종.
    /// 수치는 BranchSkillDef의 레벨별 Values/Chances에서 읽는다.
    /// </summary>
    public enum BranchSkillType
    {
        None = 0,

        // 궁수 - 명사수
        Headshot = 1,          // Values=추가 피해, Chances=일반 몬스터 즉사 확률. 공격 60회마다 발동
        CriticalAim = 2,       // Chances=크리티컬 확률 (2배 피해)

        // 궁수 - 불법 총포상
        MachineGunBurst = 3,   // Values=지속 초. 15초마다 공격주기 80% 감소
        ArmorShredShot = 4,    // Chances=물리 방어 1단계 영구 감소 확률

        // 병영 - 기사단
        HolyDuty = 5,          // Values=쿨타임 초. 체력 1 이하로 떨어지면 100% 회복
        HolySmite = 6,         // Values=광역 피해 배수, Chances=발동 확률
        IronWill = 7,          // Values=최대 체력 증가. 3레벨에 방어 1단계 추가

        // 병영 - 영웅 용병단
        BountyCollect = 8,     // Values=처치 골드 배수 (올림, 전장 회수와 합연산)
        FastRecruit = 9,       // Values=부활 대기 감소 초
        GearMod = 10,          // Values=공격력 증가. 3레벨에 15% 확률 방어 무시

        // 마법사 - 흑마법사
        DeathRay = 11,         // Values=마법 피해. 20초마다 사거리 내 체력 최다 적에게
        BloodFeast = 12,       // Values=다음 기본 공격 피해 증가율 (중첩 없음)

        // 마법사 - 환영술사
        LostWanderer = 13,     // Chances=시작 지점으로 되돌릴 확률 (중간 보스급 이상 제외)
        SnapOut = 14,          // Values=대상 수. 30초마다 3초간 서로 공격 (마법사의 공격으로 간주)

        // 포병 - 용의 숨결포
        DragonBreath = 15,     // Values=장판 초당 피해. 6초마다 다음 공격이 3초 화염 장판
        LavaShell = 16,        // Values=광역 범위 증가율. 맞은 적 20% 둔화

        // 포병 - 마개조 장사정포
        GlideShell = 17,       // Values=최대 추가 피해율 (거리에 비례해 증가)
        ClusterRocket = 18,    // Values=발사 수. 12초마다 무작위 적에게 동시 발사 + 1초 기절
    }

    /// <summary>
    /// 분기 스킬 1개. 3레벨이며 레벨별 가격은 총액의 20% / 32% / 48% (타워 증축 곡선과 같은 기울기).
    /// </summary>
    [Serializable]
    public class BranchSkillDef
    {
        public string DisplayName;
        [TextArea] public string Description;
        public BranchSkillType Type;

        [Tooltip("3레벨 총액. 레벨별 가격 = 총액의 20/32/48%")]
        public int TotalCost;

        [Tooltip("레벨별 주 수치 (피해량/배수/횟수 등)")]
        public float[] Values = new float[3];

        [Tooltip("레벨별 확률 수치 (0~1)")]
        public float[] Chances = new float[3];

        public const int MaxLevel = 3;

        static readonly float[] CostSplit = { 0.20f, 0.32f, 0.48f };

        /// <summary>levelIndex(0~2) 레벨을 올리는 비용.</summary>
        public int CostOfLevel(int levelIndex)
        {
            if (levelIndex < 0 || levelIndex >= MaxLevel)
                return 0;

            return Mathf.RoundToInt(TotalCost * CostSplit[levelIndex] / 5f) * 5;
        }

        /// <summary>level(1~3) 기준 주 수치. 0레벨(미보유)은 0.</summary>
        public float ValueAt(int level)
        {
            if (level <= 0 || Values == null || Values.Length == 0)
                return 0f;

            return Values[Mathf.Clamp(level, 1, Values.Length) - 1];
        }

        public float ChanceAt(int level)
        {
            if (level <= 0 || Chances == null || Chances.Length == 0)
                return 0f;

            return Chances[Mathf.Clamp(level, 1, Chances.Length) - 1];
        }
    }

    /// <summary>최종 분기 1개 = 증축 스탯 + 전용 스킬 2~3개. 되돌릴 수 없다.</summary>
    [Serializable]
    public class TowerBranchDef
    {
        public string Name;
        public TowerLevelStat Stat;
        public BranchSkillDef[] Skills;

        public bool IsValid => Stat != null && !string.IsNullOrEmpty(Name);
    }
}
