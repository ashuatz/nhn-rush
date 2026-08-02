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

    /// <summary>웨이브 1개 = 스폰 묶음 목록.</summary>
    [Serializable]
    public class WaveData
    {
        public SpawnEntry[] Entries;
    }

    /// <summary>스테이지 1개의 정의 (10웨이브, 약 5분). 기획서(코어 룰) 1장.</summary>
    [CreateAssetMenu(menuName = "Rush/Stage Data", fileName = "StageData")]
    public class StageData : ScriptableObject
    {
        [Header("시작 자원")]
        public int StartLife = 20;
        public int StartGold = 260;

        [Header("웨이브 페이싱")]
        public float FirstWaveDelay = 15f;
        public float WaveInterval = 30f;

        [Header("조기소환 보너스 (남은 초당 골드)")]
        public float EarlyCallGoldPerSecond = 2f;

        public WaveData[] Waves = new WaveData[10];
    }
}
