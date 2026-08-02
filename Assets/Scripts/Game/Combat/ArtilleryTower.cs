namespace Rush.Combat
{
    /// <summary>포병 계열: 착탄 지점 광역 물리 피해 + 장갑 관통. 공중 공격 불가. 기획서(코어 룰) 2장.</summary>
    public class ArtilleryTower : Tower
    {
        protected override bool TryAttack()
        {
            return TryRangedAttack();
        }
    }
}
