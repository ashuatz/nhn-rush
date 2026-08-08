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

    /// <summary>
    /// 웨이브 1개. 예산(그 웨이브에서 벌 수 있는 최대 골드)과 스탯 배수를 갖는다.
    /// 고정 Entries를 먼저 스폰하고, 남은 예산(Budget - Entries 단가 합)은
    /// RandomPool에서 무작위 구성으로 채운다. 스프레드시트(웨이브 구성예산).
    /// </summary>
    [Serializable]
    public class WaveData
    {
        [Tooltip("웨이브 예산 = 이 웨이브에서 벌 수 있는 최대 골드 (스탯 배수 반영 단가 기준)")]
        public int Budget;

        [Tooltip("체력/공격력/킬 보상에 함께 곱하는 배수. 방어 단계에는 적용하지 않는다")]
        public float StatMultiplier = 1f;

        [Tooltip("중간 보스 웨이브 여부 (6/12/18). 시작 시 보상 제시를 생략하고 보스 처치 보상을 준다")]
        public bool IsBossWave;

        [Tooltip("이 웨이브가 쓸 루트 ID (A1/A2/B1/B2). 비우면 전체 루트를 순번대로 쓴다")]
        public string[] RouteIds;

        [Tooltip("고정 스폰 구성. 남은 예산이 있으면 무작위 구성이 뒤에 붙는다")]
        public SpawnEntry[] Entries;
    }

    /// <summary>스테이지 1개의 정의 (24웨이브). 기획서(코어 룰) 1장, 스프레드시트(웨이브 구성예산).</summary>
    [CreateAssetMenu(menuName = "Rush/Stage Data", fileName = "StageData")]
    public class StageData : ScriptableObject
    {
        [Header("시작 자원")]
        public int StartLife = 20;
        public int StartGold = 300;

        [Header("웨이브 페이싱")]
        public float FirstWaveDelay = 15f;
        public float WaveInterval = 30f;

        [Header("조기소환 보너스 (다음 웨이브 예산의 비율)")]
        [Range(0f, 1f)] public float EarlyCallBudgetFraction = 0.15f;

        [Header("무작위 구성")]
        [Tooltip("무작위 구성에 쓰는 적 풀. 단가 = 기본 킬 보상 x 웨이브 배수")]
        public MonsterData[] RandomPool;

        [Tooltip("무작위 구성 스폰 간격 (초)")]
        public float RandomSpawnInterval = 0.8f;

        public WaveData[] Waves = new WaveData[24];
    }
}
