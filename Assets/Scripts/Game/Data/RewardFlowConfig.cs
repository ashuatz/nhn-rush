using UnityEngine;

namespace Rush.Data
{
    /// <summary>
    /// 보상 제시 플로우의 수치. 카드 수치와 함께 Balance Board에서 조절한다.
    /// 흐름: 웨이브 시작 직전 디밍 -> 3장 제시 -> 선택 / 다시뽑기 / 건너뛰기(+골드).
    /// </summary>
    [CreateAssetMenu(menuName = "Rush/Reward Flow Config", fileName = "RewardFlowConfig")]
    public class RewardFlowConfig : ScriptableObject
    {
        [Header("제시 시점")]
        [Tooltip("이 웨이브부터 시작 직전에 보상을 제시한다 (1이면 첫 웨이브 전부터)")]
        [Min(1)] public int FirstRewardWave = 2;
        [Tooltip("N웨이브마다 제시 (1이면 매 웨이브)")]
        [Min(1)] public int EveryNWaves = 1;

        [Header("제시 구성")]
        [Min(1)] public int CardsPerOffer = 3;
        [Min(0)] public int RerollsPerOffer = 1;
        [Min(0)] public int RerollCost = 0;
        [Min(0)] public int SkipGold = 40;

        [Header("등급 가중치")]
        [Min(0f)] public float WeightCommon = 60f;
        [Min(0f)] public float WeightRare = 25f;
        [Min(0f)] public float WeightHeroic = 11f;
        [Min(0f)] public float WeightLegendary = 4f;

        [Header("카드 풀 (셋업이 자동으로 채움)")]
        public RewardDefinition[] Cards;

        public float WeightOf(RewardRarity rarity)
        {
            if (rarity == RewardRarity.Common)
                return WeightCommon;

            if (rarity == RewardRarity.Rare)
                return WeightRare;

            if (rarity == RewardRarity.Heroic)
                return WeightHeroic;

            return WeightLegendary;
        }
    }
}
