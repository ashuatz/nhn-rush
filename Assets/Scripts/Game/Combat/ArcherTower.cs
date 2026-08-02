namespace Rush.Combat
{
    /// <summary>궁병 계열: 원거리 단일 표적 물리 공격. 공중 요격 가능. 기획서(코어 룰) 2장.</summary>
    public class ArcherTower : Tower
    {
        protected override bool TryAttack()
        {
            return TryRangedAttack();
        }
    }
}
