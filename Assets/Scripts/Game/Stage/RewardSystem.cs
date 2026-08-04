using System;
using System.Collections.Generic;
using Rush.Combat;
using Rush.Data;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Rush.Stage
{
    /// <summary>
    /// 로그라이트 보상 시스템.
    /// 웨이브 시작 직전에 게임을 멈추고(디밍) 카드 3장을 제시한다: 선택 / 다시뽑기 / 건너뛰기(+골드).
    /// 보유한 카드의 효과는 전투 코드가 static 쿼리로 읽어 간다 (씬에 배치, 부트스트랩 없음).
    /// 수치는 전부 RewardDefinition/RewardFlowConfig 에셋에 있고 Balance Board에서 조절한다.
    /// </summary>
    public class RewardSystem : MonoBehaviour
    {
        /// <summary>현재 씬의 인스턴스. 전투 코드의 static 쿼리 창구 (씬 전환 시 자동 해제).</summary>
        public static RewardSystem Active { get; private set; }

        [SerializeField] StageController _stage;
        [SerializeField] RewardFlowConfig _config;

        readonly Dictionary<RewardDefinition, int> _stacks = new Dictionary<RewardDefinition, int>();
        readonly List<RewardDefinition> _offer = new List<RewardDefinition>(3);
        readonly List<RewardDefinition> _candidateBuffer = new List<RewardDefinition>(64);
        readonly List<Monster> _monsterBuffer = new List<Monster>(32);

        Action _pendingProceed;
        int _rerollsLeft;

        public RewardFlowConfig Config => _config;

        public bool OfferActive { get; private set; }

        public IReadOnlyList<RewardDefinition> CurrentOffer => _offer;

        public int RerollsLeft => _rerollsLeft;

        /// <summary>제시 시작/변경/종료 시 발화. UI는 이것만 구독한다.</summary>
        public event Action OfferChanged;

        void Awake()
        {
            Active = this;
            _stacks.Clear();
        }

        void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }

        public int StackOf(RewardDefinition def)
        {
            if (def == null)
                return 0;

            _stacks.TryGetValue(def, out int count);

            return count;
        }

        /// <summary>보유 카드 요약 (디버그/UI용).</summary>
        public IReadOnlyDictionary<RewardDefinition, int> OwnedStacks => _stacks;

        // ---------- 제시 플로우 ----------

        /// <summary>
        /// 웨이브 시작을 가로챈다. 제시가 열리면 true를 돌려주고, 선택이 끝나면 proceed를 호출한다.
        /// </summary>
        public bool TryInterceptWaveStart(int waveNumber, Action proceed)
        {
            if (OfferActive)
                return false;

            if (_config == null || _stage == null)
                return false;

            if (waveNumber < _config.FirstRewardWave)
                return false;

            if ((waveNumber - _config.FirstRewardWave) % Mathf.Max(1, _config.EveryNWaves) != 0)
                return false;

            if (!BuildOffer())
                return false;

            _pendingProceed = proceed;
            _rerollsLeft = _config.RerollsPerOffer;
            OfferActive = true;

            // 디밍 동안 게임을 완전히 멈춘다 (스폰 코루틴 포함)
            Time.timeScale = 0f;

            GameLog.Info("Reward", $"웨이브 {waveNumber} 보상 제시");

            OfferChanged?.Invoke();

            return true;
        }

        bool BuildOffer()
        {
            _offer.Clear();
            _candidateBuffer.Clear();

            if (_config.Cards == null)
                return false;

            foreach (var card in _config.Cards)
            {
                if (card == null || !card.Enabled)
                    continue;

                if (card.Effect == RewardEffectType.None || card.Effect == RewardEffectType.DamageRangeNarrow)
                    continue;

                if (StackOf(card) >= card.StackLimit)
                    continue;

                _candidateBuffer.Add(card);
            }

            if (_candidateBuffer.Count == 0)
                return false;

            int cardCount = Mathf.Min(_config.CardsPerOffer, _candidateBuffer.Count);

            for (int i = 0; i < cardCount; i++)
            {
                var picked = PickWeighted(_candidateBuffer, _offer);

                if (picked == null)
                    break;

                _offer.Add(picked);
            }

            return _offer.Count > 0;
        }

        RewardDefinition PickWeighted(List<RewardDefinition> candidates, List<RewardDefinition> exclude)
        {
            // 등급을 가중치로 뽑고, 그 등급 안에서 균등 추첨한다. 해당 등급에 후보가 없으면 전체에서 뽑는다.
            float total = 0f;

            foreach (RewardRarity rarity in Enum.GetValues(typeof(RewardRarity)))
            {
                if (HasCandidateOfRarity(candidates, exclude, rarity))
                    total += _config.WeightOf(rarity);
            }

            if (total <= 0f)
                return PickUniform(candidates, exclude);

            float roll = Random.value * total;

            foreach (RewardRarity rarity in Enum.GetValues(typeof(RewardRarity)))
            {
                if (!HasCandidateOfRarity(candidates, exclude, rarity))
                    continue;

                roll -= _config.WeightOf(rarity);

                if (roll > 0f)
                    continue;

                return PickUniformOfRarity(candidates, exclude, rarity);
            }

            return PickUniform(candidates, exclude);
        }

        bool HasCandidateOfRarity(List<RewardDefinition> candidates, List<RewardDefinition> exclude, RewardRarity rarity)
        {
            foreach (var card in candidates)
            {
                if (card.Rarity != rarity)
                    continue;

                if (exclude.Contains(card))
                    continue;

                return true;
            }

            return false;
        }

        RewardDefinition PickUniformOfRarity(List<RewardDefinition> candidates, List<RewardDefinition> exclude, RewardRarity rarity)
        {
            int count = 0;

            foreach (var card in candidates)
            {
                if (card.Rarity == rarity && !exclude.Contains(card))
                    count++;
            }

            if (count == 0)
                return null;

            int pick = Random.Range(0, count);

            foreach (var card in candidates)
            {
                if (card.Rarity != rarity || exclude.Contains(card))
                    continue;

                if (pick == 0)
                    return card;

                pick--;
            }

            return null;
        }

        RewardDefinition PickUniform(List<RewardDefinition> candidates, List<RewardDefinition> exclude)
        {
            int count = 0;

            foreach (var card in candidates)
            {
                if (!exclude.Contains(card))
                    count++;
            }

            if (count == 0)
                return null;

            int pick = Random.Range(0, count);

            foreach (var card in candidates)
            {
                if (exclude.Contains(card))
                    continue;

                if (pick == 0)
                    return card;

                pick--;
            }

            return null;
        }

        public void Pick(int index)
        {
            if (!OfferActive)
                return;

            if (index < 0 || index >= _offer.Count)
                return;

            var card = _offer[index];

            _stacks.TryGetValue(card, out int count);
            _stacks[card] = count + 1;

            ApplyImmediate(card);

            GameLog.Info("Reward", $"[{card.Id}] {card.DisplayName} 획득 ({count + 1}/{card.StackLimit})");

            CloseOffer();
        }

        public bool CanReroll
        {
            get
            {
                if (!OfferActive)
                    return false;

                if (_rerollsLeft <= 0)
                    return false;

                if (_config.RerollCost > 0 && _stage.Gold < _config.RerollCost)
                    return false;

                return true;
            }
        }

        public void Reroll()
        {
            if (!CanReroll)
                return;

            if (_config.RerollCost > 0 && !_stage.TrySpend(_config.RerollCost))
                return;

            _rerollsLeft--;

            BuildOffer();
            GameLog.Info("Reward", "보상 다시뽑기");

            OfferChanged?.Invoke();
        }

        public void Skip()
        {
            if (!OfferActive)
                return;

            _stage.AddGold(_config.SkipGold);
            GameLog.Info("Reward", $"보상 건너뛰기 (+{_config.SkipGold}G)");

            CloseOffer();
        }

        void CloseOffer()
        {
            OfferActive = false;
            _offer.Clear();

            // 선택 전 배속을 복원한다
            if (_stage != null)
                _stage.ReapplySpeed();

            OfferChanged?.Invoke();

            var proceed = _pendingProceed;
            _pendingProceed = null;

            proceed?.Invoke();
        }

        void ApplyImmediate(RewardDefinition card)
        {
            // B3A: 즉시 골드
            if (card.Effect == RewardEffectType.InstantAndWaveGold)
                _stage.AddGold(Mathf.RoundToInt(card.Value));
        }

        // ---------- 집계 헬퍼 ----------

        /// <summary>보유 카드를 순회한다. 효과 계산의 공통 루프.</summary>
        void ForEachOwned(Action<RewardDefinition, int> visit)
        {
            foreach (var pair in _stacks)
            {
                if (pair.Key == null || pair.Value <= 0)
                    continue;

                visit(pair.Key, pair.Value);
            }
        }

        static bool SourceMatches(RewardDefinition def, in DamageSource source)
        {
            if (!def.AppliesTo(source.TowerType))
                return false;

            if (def.Tag != DamageTag.None && def.Tag != source.Tag)
                return false;

            return true;
        }

        // ---------- 전투 쿼리 (static, 미보유/미배치 시 중립값) ----------

        /// <summary>타워 종류별 스탯 배율 묶음.</summary>
        public struct TowerStatMods
        {
            public float DamageMul;
            public float AttackSpeedMul;
            public float RangeMul;
            public float SplashMul;
            public float SlowPercentAdd;
            public float SlowDurationMul;
            public float SlowAddDuration;
            public float SoldierHpMul;
            public float SoldierDamageMul;
            public float SoldierRespawnMul;
            public float RallyRangeMul;

            public static TowerStatMods Neutral
            {
                get
                {
                    return new TowerStatMods
                    {
                        DamageMul = 1f,
                        AttackSpeedMul = 1f,
                        RangeMul = 1f,
                        SplashMul = 1f,
                        SlowPercentAdd = 0f,
                        SlowDurationMul = 1f,
                        SlowAddDuration = 0f,
                        SoldierHpMul = 1f,
                        SoldierDamageMul = 1f,
                        SoldierRespawnMul = 1f,
                        RallyRangeMul = 1f,
                    };
                }
            }
        }

        public static TowerStatMods GetStatMods(TowerType type)
        {
            var mods = TowerStatMods.Neutral;

            if (Active == null)
                return mods;

            Active.ForEachOwned((def, stacks) =>
            {
                if (!def.AppliesTo(type))
                    return;

                float v = def.Value * stacks;

                switch (def.Effect)
                {
                    case RewardEffectType.DamagePercentTower:
                        mods.DamageMul += v;
                        break;
                    case RewardEffectType.AttackSpeedPercent:
                        mods.AttackSpeedMul += v;
                        break;
                    case RewardEffectType.RangePercent:
                        mods.RangeMul += v;
                        break;
                    case RewardEffectType.SplashRadiusPercent:
                        mods.SplashMul += v;
                        break;
                    case RewardEffectType.SlowDurationPercent:
                        mods.SlowDurationMul += v;
                        break;
                    case RewardEffectType.OnHitSlowAdd:
                        mods.SlowPercentAdd += v;
                        mods.SlowAddDuration = Mathf.Max(mods.SlowAddDuration, def.Duration);
                        break;
                    case RewardEffectType.SoldierHpPercent:
                        mods.SoldierHpMul += v;
                        break;
                    case RewardEffectType.SoldierDamagePercent:
                        mods.SoldierDamageMul += v;
                        break;
                    case RewardEffectType.SoldierRespawnSpeed:
                        mods.SoldierRespawnMul += v;
                        break;
                    case RewardEffectType.RallyRangePercent:
                        mods.RallyRangeMul += v;
                        break;
                }
            });

            return mods;
        }

        /// <summary>발사 시점 조건부 피해 배율 (환경 조건: 사거리/적 수/인접 타워/연속 타격/증축).</summary>
        public static float FireTimeDamageMultiplier(Tower tower, Monster target)
        {
            if (Active == null || tower == null)
                return 1f;

            float bonus = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (!def.AppliesTo(tower.Data.Type))
                    return;

                switch (def.Effect)
                {
                    case RewardEffectType.DamageIfLongRange:
                        if (tower.EffectiveRange >= def.Value2)
                            bonus += def.Value * stacks;
                        break;

                    case RewardEffectType.DamagePerEnemyInRange:
                    {
                        int count = Active.CountMonstersInRange(tower);
                        bonus += Mathf.Min(count * def.Value, def.Value2) * stacks;
                        break;
                    }

                    case RewardEffectType.DamageIfFewEnemies:
                    {
                        int count = Active.CountMonstersInRange(tower);

                        if (count > 0 && count <= Mathf.RoundToInt(def.Value2))
                            bonus += def.Value * stacks;
                        break;
                    }

                    case RewardEffectType.DamagePerNearbyTower:
                    {
                        int count = Tower.CountTowersInRange(tower);
                        float raw = count * def.Value * stacks;

                        if (def.Value2 > 0f)
                            raw = Mathf.Min(raw, def.Value2);

                        bonus += raw;
                        break;
                    }

                    case RewardEffectType.ConsecutiveHitStack:
                        bonus += tower.ConsecutiveHitBonus(target, def.Value, def.Value2);
                        break;

                    case RewardEffectType.UpgradeCostAndDamage:
                        if (tower.LevelIndex > 0)
                            bonus += def.Value2 * stacks;
                        break;
                }
            });

            return 1f + bonus;
        }

        int CountMonstersInRange(Tower tower)
        {
            MonsterRegistry.CollectInRange(tower.transform.position, tower.EffectiveRange, true, _monsterBuffer);

            return _monsterBuffer.Count;
        }

        /// <summary>착탄 시점 조건부 피해 배율 (표적 상태: 통제/저지/저항 0/풀피 + 태그 보너스).</summary>
        public static float TargetDamageMultiplier(in DamageSource source, Monster target, DamageType type)
        {
            if (Active == null || target == null)
                return 1f;

            float bonus = 0f;
            var src = source;

            Active.ForEachOwned((def, stacks) =>
            {
                switch (def.Effect)
                {
                    case RewardEffectType.DamagePercentTag:
                        if (def.Tag == src.Tag)
                            bonus += def.Value * stacks;
                        break;

                    case RewardEffectType.DamageVsControlled:
                        if (target.IsControlled)
                            bonus += def.Value * stacks;
                        break;

                    case RewardEffectType.DamageVsBlocked:
                        if (target.IsBlocked)
                            bonus += def.Value * stacks;
                        break;

                    case RewardEffectType.DamageVsResistZero:
                        if (type == DamageType.Magical && target.MagicStage == 0)
                            bonus += def.Value * stacks;
                        break;

                    case RewardEffectType.DamageVsFullHp:
                        if (src.Tag == DamageTag.Single && target.Hp >= target.MaxHp - 0.01f)
                            bonus += def.Value * stacks;
                        break;
                }
            });

            return 1f + bonus;
        }

        /// <summary>물리 방어를 무시할 확률 (독립 확률 결합).</summary>
        public static float PhysPierceChance(in DamageSource source)
        {
            if (Active == null)
                return 0f;

            float keep = 1f;
            var src = source;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect != RewardEffectType.IgnorePhysDefChance)
                    return;

                if (!SourceMatches(def, src))
                    return;

                for (int i = 0; i < stacks; i++)
                    keep *= 1f - def.Chance;
            });

            return 1f - keep;
        }

        public static bool MagicIgnoresResist()
        {
            if (Active == null)
                return false;

            bool owned = false;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.IgnoreMagicResistAll)
                    owned = true;
            });

            return owned;
        }

        /// <summary>마법 피격 시 저항 영구 하락 확률 (P03 + G05 합산).</summary>
        public static float MagicResistShredChance()
        {
            if (Active == null)
                return 0f;

            float chance = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.MagicResistShredChance)
                    chance += def.Chance * stacks;
            });

            return Mathf.Clamp01(chance);
        }

        /// <summary>통제 상태 적의 공격력 감소율 (C11).</summary>
        public static float ControlledAttackReduction()
        {
            if (Active == null)
                return 0f;

            float value = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.ControlledAttackWeaken)
                    value += def.Value * stacks;
            });

            return Mathf.Clamp(value, 0f, 0.9f);
        }

        // ---------- 병사 쿼리 ----------

        public static int SoldierMaxBlock()
        {
            int max = 1;

            if (Active == null)
                return max;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.SoldierMultiBlock)
                    max += Mathf.RoundToInt(def.Value) * stacks;
            });

            return max;
        }

        public static float SoldierDamageReduction()
        {
            if (Active == null)
                return 0f;

            float value = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.SoldierDamageReduction)
                    value += def.Value * stacks;
            });

            return Mathf.Clamp(value, 0f, 0.8f);
        }

        /// <summary>병사 공격이 확률로 적을 밀어낸다 (B1B).</summary>
        public static void TrySoldierKnockback(Monster target)
        {
            if (Active == null || target == null || !target.IsAlive)
                return;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect != RewardEffectType.SoldierKnockbackChance)
                    return;

                if (Random.value < def.Chance)
                    target.Knockback(def.Value2);
            });
        }

        // ---------- 착탄 부가 효과 ----------

        /// <summary>착탄 부가 효과 (넉백/기절). Projectile이 피해 적용 후 표적마다 호출한다.</summary>
        public static void ApplyOnHitRiders(in DamageSource source, Monster target)
        {
            if (Active == null || target == null || !target.IsAlive)
                return;

            var src = source;

            Active.ForEachOwned((def, stacks) =>
            {
                switch (def.Effect)
                {
                    case RewardEffectType.KnockbackChance:
                        if (SourceMatches(def, src) && Random.value < def.Chance)
                            target.Knockback(def.Value2);
                        break;

                    case RewardEffectType.StunChance:
                        if (SourceMatches(def, src) && Random.value < def.Chance)
                            target.ApplyStun(def.Duration, def.Value2);
                        break;
                }
            });
        }

        /// <summary>포병 폭발 중심 보너스 배율 (D08). 없으면 0.</summary>
        public static float SplashCenterBonus(in DamageSource source)
        {
            if (Active == null)
                return 0f;

            float value = 0f;
            var src = source;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.SplashCenterBonus && SourceMatches(def, src))
                    value += def.Value * stacks;
            });

            return value;
        }

        /// <summary>집속탄 (D09): 2차 폭발 비율/지연. 없으면 fraction 0.</summary>
        public static bool TryGetDoubleBlast(in DamageSource source, out float fraction, out float delay)
        {
            fraction = 0f;
            delay = 0.3f;

            if (Active == null)
                return false;

            float f = 0f;
            float d = 0.3f;
            var src = source;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect != RewardEffectType.DoubleBlast || !SourceMatches(def, src))
                    return;

                f = def.Value;

                if (def.Duration > 0f)
                    d = def.Duration;
            });

            fraction = f;
            delay = d;

            return f > 0f;
        }

        /// <summary>융단 폭격 (B2B): 포병이 두 발로 나뉘어 발사. 없으면 fraction 0.</summary>
        public static float SplitShotFraction(TowerType type)
        {
            if (Active == null)
                return 0f;

            float value = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.ArtillerySplitShot && def.AppliesTo(type))
                    value = def.Value;
            });

            return value;
        }

        /// <summary>연쇄 반응 (G02): 광역 처치 시 폭발 비율/반경. 없으면 fraction 0.</summary>
        public static bool TryGetChainExplosion(in DamageSource source, out float fraction, out float radius)
        {
            fraction = 0f;
            radius = 1.2f;

            if (Active == null || source.IsChain || source.Tag != DamageTag.Splash)
                return false;

            float f = 0f;
            float r = 1.2f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect != RewardEffectType.ChainExplosion)
                    return;

                f = def.Value;

                if (def.Value2 > 0f)
                    r = def.Value2;
            });

            fraction = f;
            radius = r;

            return f > 0f;
        }

        // ---------- 경제 쿼리 ----------

        /// <summary>처치 보너스 골드 (기본 골드에 더해진다).</summary>
        public static int KillGoldBonus(Monster monster)
        {
            if (Active == null || monster == null)
                return 0;

            var source = monster.LastHitSource;
            float percent = 0f;
            float flat = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                switch (def.Effect)
                {
                    case RewardEffectType.KillGoldPercent:
                        if (def.AppliesTo(source.TowerType))
                            percent += def.Value * stacks;
                        break;

                    case RewardEffectType.KillGoldFlatTag:
                        if (def.Tag == source.Tag)
                            flat += def.Value * stacks;
                        break;

                    case RewardEffectType.KillGoldFlatControlled:
                        if (monster.ControlledAtLastHit)
                            flat += def.Value * stacks;
                        break;
                }
            });

            return Mathf.RoundToInt(monster.Data.GoldReward * percent + flat);
        }

        /// <summary>웨이브 시작 수입 (이자 + 채권). StageController가 웨이브 시작 시 호출.</summary>
        public static int WaveStartGold(int currentGold)
        {
            if (Active == null)
                return 0;

            float total = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                switch (def.Effect)
                {
                    case RewardEffectType.WaveStartInterest:
                        total += currentGold * def.Value * stacks;
                        break;

                    case RewardEffectType.InstantAndWaveGold:
                        total += def.Value2 * stacks;
                        break;
                }
            });

            return Mathf.RoundToInt(total);
        }

        public static int SoldierRespawnGold()
        {
            if (Active == null)
                return 0;

            float total = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.GoldOnSoldierRespawn)
                    total += def.Value * stacks;
            });

            return Mathf.RoundToInt(total);
        }

        /// <summary>판매 환급 비율. 기본 90%, C04 보유 시 상향.</summary>
        public static float SellRefundFraction(float baseFraction)
        {
            if (Active == null)
                return baseFraction;

            float best = baseFraction;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.SellRefundFull)
                    best = Mathf.Max(best, def.Value);
            });

            return best;
        }

        /// <summary>업그레이드 비용 배율 (C03의 대가).</summary>
        public static float UpgradeCostMultiplier()
        {
            if (Active == null)
                return 1f;

            float mul = 1f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.UpgradeCostAndDamage)
                    mul += def.Value * stacks;
            });

            return mul;
        }
    }
}
