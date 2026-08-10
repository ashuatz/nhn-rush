using System;
using UnityEngine;

namespace Rush.Data
{
    /// <summary>웨이브 안의 스폰 묶음 하나. 같은 몬스터를 Count마리, Interval 간격으로 낸다.</summary>
    [Serializable]
    public class SpawnEntry
    {
        public MonsterData Monster;
        public int Count = 1;
        public float Interval = 1f;
        public float StartDelay;
    }

    /// <summary>중간 보스 종류 3종. 스프레드시트(중간보스 3종).</summary>
    public enum BossArchetype
    {
        Charger = 0,   // 돌격대장
        Priest = 1,    // 타락한 사제
        Siege = 2,     // 공성 책사
    }

    /// <summary>
    /// 중간 보스 종류 1개. 시트 기준으로 체력/공격력/킬 보상은 웨이브가 정하고
    /// 방어 배분과 체력 배율은 종류가 정한다. 6/12/18에 무작위 순열로 하나씩 배정된다.
    /// </summary>
    [Serializable]
    public class BossArchetypeDef
    {
        public BossArchetype Archetype;
        public string DisplayName;

        [Tooltip("이 종류가 정하는 체력 배율 (돌격대장 0.80 / 타락한 사제 0.90 / 공성 책사 1.00)")]
        public float HpScale = 1f;

        public DefenseGrade PhysicalDefense = DefenseGrade.High;
        public DefenseGrade MagicalDefense = DefenseGrade.High;
    }

    /// <summary>아키타입 구성원 1종. 예산의 Share 비율을 이 적에게 쓴다.</summary>
    [Serializable]
    public class ArchetypeMember
    {
        public MonsterData Monster;

        [Range(0f, 1f)]
        [Tooltip("이 구성원이 받을 예산 비율. 아키타입 안에서 합이 1이 되게 둔다")]
        public float Share = 1f;
    }

    /// <summary>
    /// 웨이브 아키타입 1개 (개떼 1 / 개떼 2 / 정예 1 / 정예 2 / 기동 / 짬).
    /// 스프레드시트(적 유닛 스폰 시스템 + 적 유닛 구성예산). 7~23웨이브에서만 쓴다.
    /// </summary>
    [Serializable]
    public class WaveArchetypeDef
    {
        public string Name;

        [Tooltip("구성원과 예산 배분. 비워두면 RandomPool 전체를 균등 배분한다 (짬)")]
        public ArchetypeMember[] Members;

        [Tooltip("웨이브 예산에 곱하는 계수")]
        public float BudgetScale = 1f;

        [Tooltip("등장 확률 3구간: 7~12 / 13~18 / 19~23")]
        public float[] BandChance = new float[3];

        [Tooltip("사용할 경로 수를 뽑는 확률. 순서대로 1개 / 2개 / 3개 / 4개")]
        public float[] RouteCountChance = new float[4];

        [Tooltip("이 웨이브 번호부터 등장 (정예 2 = 13). 0이면 제한 없음")]
        public int MinWave;

        [Tooltip("직전 등장과 최소 웨이브 간격 (개떼 1 = 4). 0이면 제한 없음")]
        public int MinGap;

        [Tooltip("보스 직후 웨이브(7/13/19)에 등장할 수 있는지")]
        public bool AllowedAfterBoss = true;

        /// <summary>구성원이 지정되지 않은 아키타입(짬)은 무작위 풀 전체를 쓴다.</summary>
        public bool UsesRandomPool
        {
            get
            {
                if (Members == null)
                    return true;

                return Members.Length == 0;
            }
        }
    }

    /// <summary>
    /// 웨이브 1개. 예산(그 웨이브에서 벌 수 있는 최대 골드)과 스탯 배수를 갖는다.
    /// 고정 Entries를 먼저 스폰하고, 남은 예산은 아키타입 배치 스폰으로 채운다.
    /// 스프레드시트(적 유닛 구성예산 / 적 유닛 스폰 시스템).
    /// </summary>
    [Serializable]
    public class WaveData
    {
        [Tooltip("웨이브 예산 = 이 웨이브에서 벌 수 있는 최대 골드 (스탯 배수 반영 단가 기준)")]
        public int Budget;

        [Tooltip("체력/공격력/킬 보상에 함께 곱하는 배수. 방어 단계에는 적용하지 않는다")]
        public float StatMultiplier = 1f;

        [Tooltip("중간 보스 웨이브 여부 (6/12/18). 보스를 처치해야 다음 웨이브로 넘어간다")]
        public bool IsBossWave;

        [Tooltip("최종 보스 웨이브 (24). 정규 스폰이 끝난 뒤에도 추가 스폰이 계속된다")]
        public bool IsFinalWave;

        [Tooltip("이 웨이브가 쓸 루트 ID (A1/A2/B1/B2). 비우면 전체 루트를 쓴다")]
        public string[] RouteIds;

        [Tooltip("RouteIds와 같은 순서의 경로 가중치 (시트: 고정 구간 경로). 비우거나 개수가 다르면 균등 분배한다")]
        public int[] RouteWeights;

        [Tooltip("보스가 쓸 단일 경로 ID. 비우면 이 웨이브가 쓸 수 있는 경로 중 무작위로 뽑는다")]
        public string BossRouteId;

        [Tooltip("고정 스폰 구성. 남은 예산이 있으면 아키타입 배치 구성이 뒤에 붙는다")]
        public SpawnEntry[] Entries;
    }

    /// <summary>스테이지 1개의 정의 (24웨이브). 기획서(코어 룰) 1장, 스프레드시트(적 유닛 구성예산).</summary>
    [CreateAssetMenu(menuName = "Rush/Stage Data", fileName = "StageData")]
    public class StageData : ScriptableObject
    {
        [Header("시작 자원")]
        public int StartLife = 20;
        public int StartGold = 550;

        [Header("웨이브 페이싱")]
        [Tooltip("2웨이브부터의 웨이브 간 대기 (초). 1웨이브는 대기 없이 시작 버튼으로 내보낸다")]
        public float WaveInterval = 80f;

        [Header("조기소환 보너스 (다음 웨이브 예산의 비율)")]
        [Tooltip("최대 지급 비율. 실제 지급액은 여기에 남은 대기 시간 비율을 곱한 값이다 (일찍 부를수록 많이 받는다)")]
        [Range(0f, 1f)] public float EarlyCallBudgetFraction = 0.15f;

        [Header("무작위 구성")]
        [Tooltip("짬 아키타입과 보스 웨이브 잔여 예산에 쓰는 적 풀. 단가 = 기본 킬 보상 x 웨이브 배수")]
        public MonsterData[] RandomPool;

        [Header("배치 스폰 (시트: 적 유닛 스폰 시스템)")]
        [Tooltip("한 웨이브의 스폰 구간 (초). 배치 간 간격 = 이 값 / 총 배치 수")]
        public float SpawnWindow = 60f;

        [Tooltip("한 배치 안에서 적을 순차로 낼 간격 (초)")]
        public float BatchInnerInterval = 0.35f;

        [Tooltip("배치 간 간격의 하한 (초). 이보다 좁아지면 배치 크기를 2배로 늘려 재계산한다")]
        public float MinBatchInterval = 2f;

        [Tooltip("배치 크기 2배 재계산 최대 횟수")]
        public int MaxBatchGrowth = 2;

        [Header("최종 보스전 (시트: 24웨이브)")]
        [Tooltip("정규 스폰이 끝난 뒤 추가 스폰 주기 (초)")]
        public float EndlessSpawnInterval = 30f;

        [Tooltip("추가 스폰 1회의 예산 = 정규 예산 x 이 비율")]
        [Range(0f, 2f)] public float EndlessBudgetFraction = 0.5f;

        [Tooltip("추가 스폰마다 적 스탯 배수에 더할 비율 (0.05 = 5%씩 누적)")]
        public float EndlessStatStep = 0.05f;

        [Header("아키타입 (7~23웨이브)")]
        public WaveArchetypeDef[] Archetypes;

        [Header("중간 보스 종류 (6/12/18에 무작위 순열 배정)")]
        public BossArchetypeDef[] BossArchetypes;

        public WaveData[] Waves = new WaveData[24];
    }
}
