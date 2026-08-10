using UnityEngine;

namespace Rush.Data
{
    /// <summary>
    /// 보상 제시 플로우의 수치. 카드 수치와 함께 Balance Board에서 조절한다.
    /// 흐름: 웨이브 시작 직전 디밍 -> 3장 제시 -> 1장 선택 (스킵 없음, 다시뽑기는 판 전체 5회).
    /// 중간 보스 처치 시에는 별도로 전설 2장이 제시되며 다시뽑기가 막힌다.
    /// 등급 확률은 하드코딩하지 않는다: 카드 가중치 = 등급 목표 확률 / 그 등급의 풀 개수.
    /// 스프레드시트(로그라이트 보상 시스템).
    /// </summary>
    [CreateAssetMenu(menuName = "Rush/Reward Flow Config", fileName = "RewardFlowConfig")]
    public class RewardFlowConfig : ScriptableObject
    {
        [Header("제시 시점")]
        [Tooltip("이 웨이브부터 시작 직전에 보상을 제시한다. 2면 1웨이브는 보상 없이 시작한다")]
        [Min(1)] public int FirstRewardWave = 2;
        [Tooltip("N웨이브마다 제시 (1이면 매 웨이브)")]
        [Min(1)] public int EveryNWaves = 1;

        [Header("제시 구성")]
        [Min(1)] public int CardsPerOffer = 3;

        [Tooltip("보스 클리어 보상(7/13/19웨이브 시작)의 제시 장수. 시트(중간보스 3종): 전설 2개 중 1개 선택, 다시 뽑기 불가")]
        [Min(1)] public int BossCardsPerOffer = 2;

        [Tooltip("다시뽑기 총 횟수. 한 판 전체에서 이만큼만 쓸 수 있고 보상으로 늘릴 수 없다")]
        [Min(0)] public int RerollsPerRun = 5;
        [Min(0)] public int RerollCost = 0;

        [Header("등급 목표 확률 (%)")]
        [Min(0f)] public float TargetCommon = 60.14f;
        [Min(0f)] public float TargetRare = 26.92f;
        [Min(0f)] public float TargetHeroic = 12.25f;
        [Min(0f)] public float TargetLegendary = 0.68f;

        [Header("카드 풀 (셋업이 자동으로 채움)")]
        public RewardDefinition[] Cards;

        public float TargetOf(RewardRarity rarity)
        {
            if (rarity == RewardRarity.Common)
                return TargetCommon;

            if (rarity == RewardRarity.Rare)
                return TargetRare;

            if (rarity == RewardRarity.Heroic)
                return TargetHeroic;

            return TargetLegendary;
        }
    }
}
