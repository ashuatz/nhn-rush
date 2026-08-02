using Rush.Data;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 모든 피해가 지나가는 단일 계산 창구. 기획서(코어 룰) 3.1, 3.2.
    /// 피해유형 x 방어등급 감쇄를 적용하고, VerboseCombat이 켜져 있으면 로그를 남긴다.
    /// </summary>
    public static class DamageResolver
    {
        // 등급별 대표 감쇄율 (기획서 3.2 구간의 중앙값): 낮음/보통/높음/매우높음/면역
        static readonly float[] Reduction = { 0.15f, 0.45f, 0.75f, 0.95f, 1f };

        public static float GetReduction(DefenseGrade grade)
        {
            return Reduction[(int)grade];
        }

        /// <summary>
        /// 몬스터에게 피해를 적용한다.
        /// armorPierce: 방어 감쇄를 무시하는 비율 (포병 장갑 관통, 0~1). 면역은 관통으로 뚫리지 않는다.
        /// </summary>
        public static void Apply(Monster target, float rawDamage, DamageType type, float armorPierce, string source)
        {
            if (target == null || !target.IsAlive)
                return;

            if (rawDamage <= 0f)
                return;

            float final = Calculate(target, rawDamage, type, armorPierce);

            if (GameLog.VerboseCombat)
                GameLog.Info("Dmg", $"{source} -> {target.Data.DisplayName}: {rawDamage:F0} -> {final:F1} ({type})");

            target.ApplyDamage(final, source);
        }

        public static float Calculate(Monster target, float rawDamage, DamageType type, float armorPierce)
        {
            // 고정 피해는 방어를 전부 무시한다
            if (type == DamageType.True)
                return rawDamage;

            DefenseGrade grade;

            if (type == DamageType.Physical)
                grade = target.Data.PhysicalDefense;
            else
                grade = target.Data.MagicalDefense;

            // 면역: 해당 유형 완전 무효 (관통 불가)
            if (grade == DefenseGrade.Immune)
                return 0f;

            float reduction = GetReduction(grade);
            reduction *= 1f - Mathf.Clamp01(armorPierce);

            return rawDamage * (1f - reduction);
        }
    }
}
