using UnityEngine;
using Random = UnityEngine.Random;

namespace Rush.Stage
{
    /// <summary>
    /// 전투 확률 판정의 단일 창구. 보상 C15(행운의 부적)가 여기에 재굴림을 얹는다.
    ///
    /// 규칙: 판정이 실패하면 재굴림 횟수만큼 다시 굴리고, 한 번이라도 성공하면 성공이다.
    /// 즉 유효 확률은 1-(1-p)^(1+재굴림)이 된다 (20% -> 재굴림 1회 시 36%).
    ///
    /// 재굴림이 0이면 Random.value < chance와 완전히 같으므로, 보상이 없을 때의 동작은 바뀌지 않는다.
    ///
    /// 여기를 타지 않는 무작위:
    /// - 이진 판정이 아닌 것 (보상 카드 가중 추첨, 웨이브 몬스터 선택, 병사 피해 범위 롤, 표적 무작위 선택)
    /// - 연출용 (투사체 궤적 산포)
    /// - 즉사 판정 (Projectile의 InstantKillChance) - 밸런스상 의도적으로 제외한다
    /// </summary>
    public static class Luck
    {
        /// <summary>현재 재굴림 횟수. 보상 카드 획득 시에만 갱신된다.</summary>
        public static int Rerolls => RewardSystem.LuckRerolls;

        /// <summary>확률 판정. 실패하면 재굴림 횟수만큼 다시 굴린다.</summary>
        public static bool Roll(float chance)
        {
            if (chance <= 0f)
                return false;

            if (chance >= 1f)
                return true;

            int tries = 1 + Mathf.Max(0, RewardSystem.LuckRerolls);

            for (int i = 0; i < tries; i++)
            {
                if (Random.value < chance)
                    return true;
            }

            return false;
        }

        /// <summary>재굴림을 반영한 유효 확률. UI/디버그 표시용이며 판정에는 쓰지 않는다.</summary>
        public static float EffectiveChance(float chance)
        {
            if (chance <= 0f)
                return 0f;

            if (chance >= 1f)
                return 1f;

            int tries = 1 + Mathf.Max(0, RewardSystem.LuckRerolls);

            return 1f - Mathf.Pow(1f - chance, tries);
        }
    }
}
