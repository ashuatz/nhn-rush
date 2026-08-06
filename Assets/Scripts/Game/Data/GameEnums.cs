namespace Rush.Data
{
    /// <summary>타워 4계열. 기획서(코어 룰) 2장.</summary>
    public enum TowerType
    {
        Infantry = 0,
        Archer = 1,
        Mage = 2,
        Artillery = 3,
    }

    /// <summary>공격/피해 유형. 기획서(코어 룰) 3.1.</summary>
    public enum DamageType
    {
        Physical = 0,
        Magical = 1,
        True = 2,
    }

    /// <summary>
    /// 방어 등급 5단계 = 시트의 0~4단계. 단계당 25% 감쇄, Immune(4단계)은 해당 속성 피해 무효.
    /// 스프레드시트(적 시스템: 방어력/마법 저항).
    /// </summary>
    public enum DefenseGrade
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Great = 3,
        Immune = 4,
    }
}
