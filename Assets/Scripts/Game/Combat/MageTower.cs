namespace Rush.Combat
{
    /// <summary>마도 계열: 마법 피해 + 슬로우. 공중 요격 가능. 기획서(코어 룰) 2장.</summary>
    public class MageTower : Tower
    {
        protected override bool TryAttack()
        {
            return TryRangedAttack();
        }
    }
}
