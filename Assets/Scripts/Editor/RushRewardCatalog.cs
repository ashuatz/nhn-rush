using System.Collections.Generic;
using Rush.Data;
using UnityEditor;
using UnityEngine;

namespace Rush.EditorTools
{
    /// <summary>
    /// 로그라이트 보상 시트(스프레드시트) 54종의 코드 사본.
    /// 시트가 기획 원본이고, 여기는 ID 기준으로 에셋을 생성/갱신하는 카탈로그다.
    /// 재실행 시 텍스트/분류(이름/설명/등급/필터/효과 종류)는 시트 기준으로 갱신하고,
    /// 수치(Value/Value2/Chance/Duration/StackLimit/Enabled)는 Balance Board에서 조정한 값을 존중한다.
    /// </summary>
    public static class RushRewardCatalog
    {
        public struct Entry
        {
            public string Id;
            public RewardCategory Cat;
            public RewardRarity Rarity;
            public string Name;
            public string Desc;
            public string Cond;
            public RewardTowerFilter Filter;
            public DamageTag Tag;
            public int Stack;
            public RewardEffectType Effect;
            public float V;
            public float V2;
            public float Chance;
            public float Dur;
            public bool Enabled;
            public string DisabledReason;
        }

        static Entry Row(string id, RewardCategory cat, RewardRarity rarity, string name, string desc, string cond,
            RewardTowerFilter filter, DamageTag tag, int stack, RewardEffectType effect,
            float v = 0f, float v2 = 0f, float chance = 0f, float dur = 0f,
            bool enabled = true, string disabledReason = "")
        {
            return new Entry
            {
                Id = id, Cat = cat, Rarity = rarity, Name = name, Desc = desc, Cond = cond,
                Filter = filter, Tag = tag, Stack = stack, Effect = effect,
                V = v, V2 = v2, Chance = chance, Dur = dur,
                Enabled = enabled, DisabledReason = disabledReason,
            };
        }

        const RewardCategory Fire = RewardCategory.Firepower;
        const RewardCategory Econ = RewardCategory.Economy;
        const RewardCategory Place = RewardCategory.Placement;
        const RewardCategory Ctrl = RewardCategory.Control;

        const RewardRarity N = RewardRarity.Common;
        const RewardRarity R = RewardRarity.Rare;
        const RewardRarity H = RewardRarity.Heroic;
        const RewardRarity L = RewardRarity.Legendary;

        const RewardTowerFilter Any = RewardTowerFilter.Any;
        const RewardTowerFilter Archer = RewardTowerFilter.Archer;
        const RewardTowerFilter Mage = RewardTowerFilter.Mage;
        const RewardTowerFilter Arty = RewardTowerFilter.Artillery;
        const RewardTowerFilter Inf = RewardTowerFilter.Infantry;

        public static readonly Entry[] Entries =
        {
            // ---------- 일반 (중첩 3) ----------
            Row("F01", Fire, N, "속사 훈련", "궁수 타워 공격속도 +10%", "", Archer, DamageTag.None, 3, RewardEffectType.AttackSpeedPercent, 0.10f),
            Row("F02", Fire, N, "마력 집중", "마법사 타워 피해 +12%", "", Mage, DamageTag.None, 3, RewardEffectType.DamagePercentTower, 0.12f),
            Row("F03", Fire, N, "고폭탄", "포병 타워 피해 +12%", "", Arty, DamageTag.None, 3, RewardEffectType.DamagePercentTower, 0.12f),
            Row("E01", Econ, N, "현상 수배", "궁수 타워가 처치한 적의 골드 +40%", "궁수가 막타를 쳐야 발동", Archer, DamageTag.None, 3, RewardEffectType.KillGoldPercent, 0.40f),
            Row("E02", Econ, N, "전장 회수", "병영 유닛이 처치한 적의 골드 +70%", "병영이 막타를 쳐야 발동 · 빈도가 낮아 배율이 높음", Inf, DamageTag.None, 3, RewardEffectType.KillGoldPercent, 0.70f),
            Row("E03", Econ, N, "잔해 수거", "광역 피해로 처치한 적당 골드 +5", "광역 태그가 막타를 쳐야 발동", Any, DamageTag.Splash, 3, RewardEffectType.KillGoldFlatTag, 5f),
            Row("E04", Fire, N, "연마된 검", "병영 유닛 피해 +12%", "병영 한정", Inf, DamageTag.None, 3, RewardEffectType.SoldierDamagePercent, 0.12f),
            Row("E05", Place, N, "장궁", "궁수 타워 사거리 +15%", "궁수 한정", Archer, DamageTag.None, 3, RewardEffectType.RangePercent, 0.15f),
            Row("E06", Place, N, "전방 전개", "병영 유닛의 배치 가능 거리 +25%", "병영 한정", Inf, DamageTag.None, 3, RewardEffectType.RallyRangePercent, 0.25f),
            Row("E07", Ctrl, N, "둔화 탄", "포병 타워 공격에 맞은 적 이동속도 -12% (1초)", "포병 한정", Arty, DamageTag.None, 3, RewardEffectType.OnHitSlowAdd, 0.12f, 0f, 0f, 1f),
            Row("E08", Ctrl, N, "마력 속박", "마법사 타워 공격에 맞은 적 이동속도 -8% (1초)", "마법사 한정", Mage, DamageTag.None, 3, RewardEffectType.OnHitSlowAdd, 0.08f, 0f, 0f, 1f),
            Row("E09", Ctrl, N, "강철 갑주", "병영 유닛 체력 +15%", "병영 한정", Inf, DamageTag.None, 3, RewardEffectType.SoldierHpPercent, 0.15f),
            Row("P01", Fire, N, "정밀 조준", "단일 피해 +10%", "단일 태그 한정", Any, DamageTag.Single, 3, RewardEffectType.DamagePercentTag, 0.10f),
            Row("P02", Fire, N, "파편 확산", "광역 피해 +10%", "광역 태그 한정", Any, DamageTag.Splash, 3, RewardEffectType.DamagePercentTag, 0.10f),
            Row("P03", Fire, N, "마력 침투", "마법 피해가 20% 확률로 대상의 마법 저항을 1단계 영구히 낮춤", "확률 판정 · 저항 0단계 적에겐 무의미", Any, DamageTag.Magic, 3, RewardEffectType.MagicResistShredChance, 0f, 0f, 0.20f),
            Row("P04", Fire, N, "관통 탄두", "포병 피해가 20% 확률로 물리 방어를 무시하고 들어감", "확률 판정 · 방어 0단계 적에겐 차이 없음", Arty, DamageTag.None, 3, RewardEffectType.IgnorePhysDefChance, 0f, 0f, 0.20f),
            Row("P05", Fire, N, "정밀 시전", "마법사 타워 피해의 최소~최대 폭이 33% 좁아짐 (3중첩 시 고정값)", "마법사 한정", Mage, DamageTag.None, 3, RewardEffectType.DamageRangeNarrow, 0.33f,
                0f, 0f, 0f, false, "피해 최소~최대 범위 시스템이 아직 없음 (현재 고정 피해)"),
            Row("P06", Fire, N, "강궁", "궁수 타워 피해 +12%", "궁수 한정", Archer, DamageTag.None, 3, RewardEffectType.DamagePercentTower, 0.12f),
            Row("C01", Fire, N, "속사 장전", "포병 타워 재장전 속도 +12%", "포병 한정", Arty, DamageTag.None, 3, RewardEffectType.AttackSpeedPercent, 0.12f),

            // ---------- 희귀 ----------
            Row("C02", Econ, R, "이자 수익", "웨이브 시작 시 보유 골드의 5% 추가 획득", "골드를 쓰지 않고 버텨야 이득", Any, DamageTag.None, 1, RewardEffectType.WaveStartInterest, 0.05f),
            Row("C03", Econ, R, "고급 자재", "증축 비용이 25% 오르는 대신 증축된 타워의 피해량 +20%", "증축을 해야 의미가 있음", Any, DamageTag.None, 1, RewardEffectType.UpgradeCostAndDamage, 0.25f, 0.20f),
            Row("C04", Econ, R, "재개발", "타워 판매 시 건설·증축 비용 100% 환급", "판매·재건축을 반복해야 가치가 남", Any, DamageTag.None, 1, RewardEffectType.SellRefundFull, 1f),
            Row("C05", Econ, R, "징집 명령", "병영 유닛 충원 속도 +25%", "병영 한정", Inf, DamageTag.None, 1, RewardEffectType.SoldierRespawnSpeed, 0.25f),
            Row("C06", Place, R, "원거리 특화", "일정 사거리 이상인 타워는 피해량 20% 증가", "", Any, DamageTag.None, 1, RewardEffectType.DamageIfLongRange, 0.20f, 5.5f),
            Row("C07", Place, R, "관측 우위", "사거리 내의 적이 많아질수록 피해량 증가 (1%씩 30%까지)", "", Any, DamageTag.None, 1, RewardEffectType.DamagePerEnemyInRange, 0.01f, 0.30f),
            Row("C08", Fire, R, "조준 유지", "같은 적을 연속 공격할 때마다 피해 +8% 누적 (최대 +40%)", "대상이 바뀌면 초기화 · 연사 계열은 이득이 적음", Any, DamageTag.None, 1, RewardEffectType.ConsecutiveHitStack, 0.08f, 0.40f),
            Row("D01", Fire, R, "약점 노출", "감속·저지 상태의 적이 받는 피해 +25%", "감속원이 없으면 완전 무효", Any, DamageTag.None, 1, RewardEffectType.DamageVsControlled, 0.25f),
            Row("D02", Ctrl, R, "강제 전송", "마법 타워 공격이 10% 확률로 대상을 뒤로 넉백", "", Mage, DamageTag.None, 1, RewardEffectType.KnockbackChance, 0f, 1.5f, 0.10f),
            Row("D03", Ctrl, R, "방벽", "병영 유닛의 물리 방어 1단계 상승", "병영 한정", Inf, DamageTag.None, 1, RewardEffectType.SoldierDamageReduction, 0.15f),
            Row("D04", Fire, R, "직격", "마법 저항 0단계인 적에게 마법 피해 +30%", "저항이 남아 있으면 무효 · 마력 침투로 0단계를 만들어야 함", Any, DamageTag.Magic, 1, RewardEffectType.DamageVsResistZero, 0.30f),
            Row("M03", Fire, R, "광역 확장", "광역 피해의 범위 +20%", "광역 태그 한정", Any, DamageTag.Splash, 1, RewardEffectType.SplashRadiusPercent, 0.20f),
            Row("M04", Fire, R, "급소 타격", "단일 피해가 25% 확률로 물리 방어를 무시하고 들어감", "확률 판정 · 방어 0단계 적에겐 차이 없음", Any, DamageTag.Single, 1, RewardEffectType.IgnorePhysDefChance, 0f, 0f, 0.25f),
            Row("D06", Fire, R, "집중 사격", "궁수 타워의 사거리 안에 적이 3기 이하일 때 그 궁수 피해 +30%", "물량 웨이브에서는 거의 켜지지 않음", Archer, DamageTag.None, 1, RewardEffectType.DamageIfFewEnemies, 0.30f, 3f),
            Row("D07", Fire, R, "관통 사격", "궁수의 화살이 15% 확률로 대상 물리 방어력을 무시", "", Archer, DamageTag.Single, 1, RewardEffectType.IgnorePhysDefChance, 0f, 0f, 0.15f),
            Row("D08", Fire, R, "직격탄", "포병 폭발의 중심에 있는 적 1기에게 피해 +50%", "적이 흩어져 있으면 중심 대상이 없음", Arty, DamageTag.Splash, 1, RewardEffectType.SplashCenterBonus, 0.50f),
            Row("D09", Fire, R, "집속탄", "포병 폭발이 2회 연속으로 터짐 (2번째는 피해 40%)", "같은 지점에 두 번 터지므로 적이 이동하면 손실", Arty, DamageTag.Splash, 1, RewardEffectType.DoubleBlast, 0.40f, 0f, 0f, 0.3f),

            // ---------- 영웅 ----------
            Row("A01", Econ, H, "현장 보급", "병영 유닛이 충원될 때마다 골드 +20", "유닛이 죽고 채워져야 수입 · 전선이 안정되면 오히려 손해", Inf, DamageTag.None, 1, RewardEffectType.GoldOnSoldierRespawn, 20f),
            Row("A02", Place, H, "거점 방어", "타워 사거리 내에 다른 타워가 많아질수록 피해량 10%씩 증가", "좁은 구간만 덮으면 거의 무효", Any, DamageTag.None, 1, RewardEffectType.DamagePerNearbyTower, 0.10f),
            Row("A03", Fire, H, "협공", "병영 유닛이 저지 중인 적이 받는 피해 +20%", "전선이 없으면 완전 무효", Any, DamageTag.None, 1, RewardEffectType.DamageVsBlocked, 0.20f),
            Row("A04", Econ, H, "통행세", "감속·저지 상태의 적 처치 시 골드 +12", "통제 상태가 아니면 수입 없음", Any, DamageTag.None, 1, RewardEffectType.KillGoldFlatControlled, 12f),
            Row("G01", Fire, H, "일격필살", "단일 피해가 체력 100%인 적에게 +80%", "첫 타격에만 적용", Any, DamageTag.Single, 1, RewardEffectType.DamageVsFullHp, 0.80f),
            Row("G02", Fire, H, "연쇄 반응", "광역 피해로 죽은 적이 폭발하여 주변 피해 (원래 피해의 40%)", "1회만 연쇄 · 단일 처치에는 무효", Any, DamageTag.Splash, 1, RewardEffectType.ChainExplosion, 0.40f, 1.2f),
            Row("G03", Fire, H, "속사 전환", "궁수 타워 공격속도 +25%", "궁수 한정", Archer, DamageTag.None, 1, RewardEffectType.AttackSpeedPercent, 0.25f),
            Row("G05", Fire, H, "마력 잠식", "마법 피해가 대상의 마법 저항을 1단계 낮출 확률 +35%p", "마력 침투와 합산", Any, DamageTag.Magic, 1, RewardEffectType.MagicResistShredChance, 0f, 0f, 0.35f),
            Row("B1A", Ctrl, H, "전선 확장", "병영 유닛 1기가 적 2기를 동시에 저지", "병영 한정 · 전선이 없으면 무효", Inf, DamageTag.None, 1, RewardEffectType.SoldierMultiBlock, 1f),
            Row("B1B", Ctrl, H, "방패 밀치기", "병영 유닛의 공격이 15% 확률로 적을 뒤로 밀어냄 · 밀려난 적은 저지가 풀려 다시 붙잡아야 함", "병영 한정", Inf, DamageTag.None, 1, RewardEffectType.SoldierKnockbackChance, 0f, 1.2f, 0.15f),
            Row("B2A", Ctrl, H, "충격파", "포병 폭발에 맞은 적이 15% 확률로 1초간 기절 · 기절이 끝나면 3초간 기절 면역", "포병 한정", Arty, DamageTag.None, 1, RewardEffectType.StunChance, 0f, 3f, 0.15f, 1f),

            // ---------- 전설 ----------
            Row("B2B", Fire, L, "융단 폭격", "포병 공격이 두 지점으로 나뉘어 착탄 (각 60% 피해)", "적이 흩어져 있어야 이득 · 단일 표적에는 총 피해가 줄어든다", Arty, DamageTag.Splash, 1, RewardEffectType.ArtillerySplitShot, 0.60f),
            Row("B3A", Econ, L, "전시 채권", "즉시 골드 +300 · 이후 웨이브 시작 골드 +80", "", Any, DamageTag.None, 1, RewardEffectType.InstantAndWaveGold, 300f, 80f),
            Row("B3B", Ctrl, L, "동절기", "모든 감속 효과의 지속시간 1.6배", "감속원이 없으면 무효", Any, DamageTag.None, 1, RewardEffectType.SlowDurationPercent, 0.60f),
            Row("M06", Place, L, "전초 기지", "궁수 타워 사거리 +25%", "궁수 한정", Archer, DamageTag.None, 1, RewardEffectType.RangePercent, 0.25f),
            Row("C10", Fire, L, "속성 각인", "마법 피해가 대상의 마법 저항을 완전히 무시 (이뮨 포함)", "마법 피해원이 없으면 무효", Any, DamageTag.Magic, 1, RewardEffectType.IgnoreMagicResistAll),
            Row("C11", Ctrl, L, "무기력", "감속·저지 상태의 적은 공격력도 30% 감소", "전선이나 감속이 없으면 무효", Any, DamageTag.None, 1, RewardEffectType.ControlledAttackWeaken, 0.30f),
            Row("C12", Fire, L, "파쇄 교리", "모든 피해가 30% 확률로 물리 방어를 무시하고 들어감", "방어 0단계 적에겐 차이 없음", Any, DamageTag.None, 1, RewardEffectType.IgnorePhysDefChance, 0f, 0f, 0.30f),
        };

        /// <summary>
        /// 카탈로그를 에셋으로 동기화한다.
        /// forceValues가 false면 수치/활성 상태는 기존 에셋 값을 존중한다 (텍스트/분류만 갱신).
        /// </summary>
        public static List<RewardDefinition> EnsureAll(string folder, bool forceValues)
        {
            var result = new List<RewardDefinition>(Entries.Length);

            foreach (var entry in Entries)
            {
                string path = $"{folder}/Reward_{entry.Id}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<RewardDefinition>(path);
                bool isNew = asset == null;

                if (isNew)
                {
                    asset = ScriptableObject.CreateInstance<RewardDefinition>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                // 텍스트/분류는 항상 카탈로그(시트) 기준으로 맞춘다
                asset.Id = entry.Id;
                asset.DisplayName = entry.Name;
                asset.Category = entry.Cat;
                asset.Rarity = entry.Rarity;
                asset.Description = entry.Desc;
                asset.ConditionNote = entry.Cond;
                asset.TowerFilter = entry.Filter;
                asset.Tag = entry.Tag;
                asset.Effect = entry.Effect;

                // 수치는 신규이거나 강제 갱신일 때만 덮어쓴다
                if (isNew || forceValues)
                {
                    asset.Value = entry.V;
                    asset.Value2 = entry.V2;
                    asset.Chance = entry.Chance;
                    asset.Duration = entry.Dur;
                    asset.StackLimit = entry.Stack;
                    asset.Enabled = entry.Enabled;
                    asset.DisabledReason = entry.DisabledReason;
                }

                EditorUtility.SetDirty(asset);
                result.Add(asset);
            }

            return result;
        }
    }
}
