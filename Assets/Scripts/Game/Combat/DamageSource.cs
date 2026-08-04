using Rush.Data;

namespace Rush.Combat
{
    /// <summary>
    /// 피해의 출처. 보상(막타 골드, 태그 보너스, 조건부 피해)이 출처를 보고 판정한다.
    /// </summary>
    public struct DamageSource
    {
        public TowerType TowerType;
        public bool HasTower;
        public bool FromSoldier;
        public DamageTag Tag;
        public Tower Tower;

        /// <summary>연쇄 폭발로 생긴 피해. 다시 연쇄를 일으키지 않는다.</summary>
        public bool IsChain;

        public string Label;

        public static DamageSource FromTower(Tower tower, DamageTag tag, string label)
        {
            return new DamageSource
            {
                TowerType = tower.Data.Type,
                HasTower = true,
                FromSoldier = false,
                Tag = tag,
                Tower = tower,
                IsChain = false,
                Label = label,
            };
        }

        public static DamageSource FromSoldierUnit(Tower owner)
        {
            var source = new DamageSource
            {
                TowerType = TowerType.Infantry,
                HasTower = owner != null,
                FromSoldier = true,
                Tag = DamageTag.Single,
                Tower = owner,
                IsChain = false,
                Label = "병사",
            };

            return source;
        }

        public DamageSource AsChain()
        {
            var copy = this;
            copy.IsChain = true;
            copy.Label = Label + " 연쇄";

            return copy;
        }
    }
}
