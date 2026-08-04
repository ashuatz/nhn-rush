using Rush.Data;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 모든 피해가 지나가는 단일 계산 창구. 기획서(코어 룰) 3.1, 3.2.
    /// 방어는 몬스터의 런타임 단계(0=없음 ~ 5=면역)로 계산하며, 보상 훅이 여기서 개입한다:
    /// 태그 피해 보너스, 조건부 피해, 확률 방어 무시, 저항 무시/영구 하락.
    /// </summary>
    public static class DamageResolver
    {
        // 단계별 감쇄율: 없음/낮음/보통/높음/매우높음/면역
        static readonly float[] StageReduction = { 0f, 0.15f, 0.45f, 0.75f, 0.95f, 1f };

        public const int ImmuneStage = 5;

        public static float GetStageReduction(int stage)
        {
            int clamped = Mathf.Clamp(stage, 0, StageReduction.Length - 1);

            return StageReduction[clamped];
        }

        /// <summary>
        /// 몬스터에게 피해를 적용한다.
        /// armorPierce: 방어 감쇄를 무시하는 비율 (포병 장갑 관통, 0~1). 면역은 관통으로 뚫리지 않는다.
        /// </summary>
        public static void Apply(Monster target, float rawDamage, DamageType type, float armorPierce, in DamageSource source)
        {
            if (target == null || !target.IsAlive)
                return;

            if (rawDamage <= 0f)
                return;

            // 보상: 태그/조건부 피해 배율은 감쇄 전에 곱한다
            float boosted = rawDamage * RewardSystem.TargetDamageMultiplier(source, target, type);

            float final = Calculate(target, boosted, type, armorPierce, source);

            if (GameLog.VerboseCombat)
                GameLog.Info("Dmg", $"{source.Label} -> {target.Data.DisplayName}: {rawDamage:F0} -> {final:F1} ({type})");

            target.ApplyDamage(final, source);

            // 보상: 마법 저항 영구 하락 (죽지 않았을 때만 의미 있음)
            if (type == DamageType.Magical && target.IsAlive)
            {
                float shred = RewardSystem.MagicResistShredChance();

                if (shred > 0f && Random.value < shred)
                    target.LowerMagicStage();
            }
        }

        public static float Calculate(Monster target, float rawDamage, DamageType type, float armorPierce, in DamageSource source)
        {
            // 고정 피해는 방어를 전부 무시한다
            if (type == DamageType.True)
                return rawDamage;

            if (type == DamageType.Physical)
                return CalculatePhysical(target, rawDamage, armorPierce, source);

            return CalculateMagical(target, rawDamage);
        }

        static float CalculatePhysical(Monster target, float rawDamage, float armorPierce, in DamageSource source)
        {
            int stage = target.PhysStage;

            // 보상: 확률 물리 방어 무시 (면역은 못 뚫는다)
            if (stage < ImmuneStage)
            {
                float pierceChance = RewardSystem.PhysPierceChance(source);

                if (pierceChance > 0f && Random.value < pierceChance)
                    return rawDamage;
            }

            if (stage >= ImmuneStage)
                return 0f;

            float reduction = GetStageReduction(stage);
            reduction *= 1f - Mathf.Clamp01(armorPierce);

            return rawDamage * (1f - reduction);
        }

        static float CalculateMagical(Monster target, float rawDamage)
        {
            // 보상: 마법 저항 완전 무시 (면역 포함, C10)
            if (RewardSystem.MagicIgnoresResist())
                return rawDamage;

            int stage = target.MagicStage;

            if (stage >= ImmuneStage)
                return 0f;

            return rawDamage * (1f - GetStageReduction(stage));
        }
    }
}
