namespace Rush.Data
{
    /// <summary>보상 등급. 스프레드시트(로그라이트 보상 시트) 기준.</summary>
    public enum RewardRarity
    {
        Common = 0,
        Rare = 1,
        Heroic = 2,
        Legendary = 3,
    }

    /// <summary>보상 계통 (표시용 분류).</summary>
    public enum RewardCategory
    {
        Firepower = 0,
        Economy = 1,
        Placement = 2,
        Control = 3,
    }

    /// <summary>피해 태그. 시트의 단일/광역/마법/고정 컬럼 대응.</summary>
    public enum DamageTag
    {
        None = 0,
        Single = 1,
        Splash = 2,
        Magic = 3,
        Fixed = 4,
    }

    /// <summary>보상이 적용되는 타워 필터.</summary>
    public enum RewardTowerFilter
    {
        Any = 0,
        Archer = 1,
        Mage = 2,
        Artillery = 3,
        Infantry = 4,
    }

    /// <summary>
    /// 보상 효과 종류. 값의 의미는 카드마다 Value/Value2/Chance/Duration 필드로 정한다.
    /// 여기 없는 효과는 아직 코드가 지원하지 않는 것이며, 그런 카드는 Enabled=false로 덱에서 빠진다.
    /// </summary>
    public enum RewardEffectType
    {
        None = 0,

        // 스탯 배율 (Value = 증가율)
        DamagePercentTower = 1,
        DamagePercentTag = 2,
        AttackSpeedPercent = 3,
        RangePercent = 4,
        SplashRadiusPercent = 5,
        SlowDurationPercent = 6,
        OnHitSlowAdd = 7,

        // 병영/병사 (Value = 증가율 또는 수치)
        SoldierDamagePercent = 20,
        SoldierHpPercent = 21,
        SoldierRespawnSpeed = 22,
        RallyRangePercent = 23,
        SoldierDamageReduction = 24,
        SoldierMultiBlock = 25,
        SoldierKnockbackChance = 26,

        // 경제 (Value/Value2 = 골드 수치)
        GoldOnSoldierRespawn = 40,
        KillGoldPercent = 41,
        KillGoldFlatTag = 42,
        KillGoldFlatControlled = 43,
        WaveStartInterest = 44,
        InstantAndWaveGold = 45,
        SellRefundFull = 46,
        UpgradeCostAndDamage = 47,

        // 방어 관통/저항 (Chance = 확률)
        IgnorePhysDefChance = 60,
        IgnoreMagicResistAll = 61,
        MagicResistShredChance = 62,

        // 조건부 피해 (Value = 증가율, Value2 = 조건 파라미터)
        DamageVsControlled = 80,
        DamageVsBlocked = 81,
        DamageVsResistZero = 82,
        DamageVsFullHp = 83,
        DamageIfLongRange = 84,
        DamagePerEnemyInRange = 85,
        DamageIfFewEnemies = 86,
        DamagePerNearbyTower = 87,
        ConsecutiveHitStack = 88,

        // 발사/착탄 메커니즘
        SplashCenterBonus = 100,
        DoubleBlast = 101,
        ArtillerySplitShot = 102,
        ChainExplosion = 103,
        KnockbackChance = 104,
        StunChance = 105,
        BonusProcShot = 106,
        BonusOnKillShot = 107,

        // 확률 판정 보정 (Value = 재굴림 횟수)
        LuckReroll = 130,

        // 미구현 (전제 시스템 없음)
        DamageRangeNarrow = 200,

        // 몬스터 약화
        ControlledAttackWeaken = 120,
    }
}
