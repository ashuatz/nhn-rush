using System;
using System.Collections.Generic;
using Rush.Combat;
using Rush.Data;
using Rush.Fx;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Rush.Stage
{
    /// <summary>
    /// 로그라이트 보상 시스템.
    /// 웨이브 시작 직전에 게임을 멈추고(디밍) 카드 3장을 제시한다: 1장 선택 / 다시뽑기(판 전체 5회).
    /// 보스 라운드 직후 웨이브(7/13/19)만 확정 전설 2장 / 다시뽑기 불가이고 나머지는 평시 구성이다.
    /// 버린(제시됐지만 안 뽑은) 카드는 같은 제시 화면(다시뽑기 포함)에 다시 나오지 않는다.
    /// 보유한 카드의 효과는 전투 코드가 static 쿼리로 읽어 간다 (씬에 배치, 부트스트랩 없음).
    /// 수치는 전부 RewardDefinition/RewardFlowConfig 에셋에 있고 Balance Board에서 조절한다.
    /// </summary>
    public class RewardSystem : MonoBehaviour
    {
        /// <summary>현재 씬의 인스턴스. 전투 코드의 static 쿼리 창구 (씬 전환 시 자동 해제).</summary>
        public static RewardSystem Active { get; private set; }

        [SerializeField] StageController _stage;
        [SerializeField] RewardFlowConfig _config;

        [Tooltip("행운(C15) 재굴림이 실패를 뒤집었을 때 그 자리에 재생할 연출")]
        [SerializeField] GameObject _luckFx;

        readonly Dictionary<RewardDefinition, int> _stacks = new Dictionary<RewardDefinition, int>();
        readonly List<RewardDefinition> _offer = new List<RewardDefinition>(3);
        readonly List<RewardDefinition> _candidateBuffer = new List<RewardDefinition>(64);
        readonly HashSet<RewardDefinition> _discardedThisOffer = new HashSet<RewardDefinition>();
        readonly List<Monster> _monsterBuffer = new List<Monster>(32);

        Action _pendingProceed;
        int _rerollsLeft;
        bool _rerollsInitialized;
        bool _offerLegendaryOnly;

        /// <summary>이번 제시의 장수와 다시뽑기 허용 여부. 보스 클리어 보상은 2장 / 다시뽑기 불가다.</summary>
        int _offerCardCount;
        bool _offerAllowReroll;

        public RewardFlowConfig Config => _config;

        public bool OfferActive { get; private set; }

        /// <summary>보유 카드 구성이 바뀔 때마다 증가. 스탯을 스냅샷한 개체(병사)가 갱신 시점을 감지한다.</summary>
        public int Version { get; private set; }

        public static int StatVersion
        {
            get
            {
                if (Active == null)
                    return 0;

                return Active.Version;
            }
        }

        /// <summary>
        /// 확률 판정 재굴림 횟수 (C15). Luck.Roll이 매 판정마다 읽으므로 카드 획득 시에만 다시 계산한다.
        /// static이라 씬을 넘어가도 남으므로 Awake/OnDestroy에서 반드시 0으로 되돌린다.
        /// </summary>
        public static int LuckRerolls { get; private set; }

        public IReadOnlyList<RewardDefinition> CurrentOffer => _offer;

        public int RerollsLeft => _rerollsLeft;

        /// <summary>이번 제시에 다시뽑기 자체가 허용되는지. 보스 클리어 보상은 횟수와 무관하게 막힌다.</summary>
        public bool RerollAllowed => _offerAllowReroll;

        /// <summary>제시 시작/변경/종료 시 발화. UI는 이것만 구독한다.</summary>
        public event Action OfferChanged;

        /// <summary>카드를 획득했을 때 발화 (획득 보상 바 연출용).</summary>
        public event Action<RewardDefinition> CardAcquired;

        void Awake()
        {
            Active = this;
            _stacks.Clear();
            LuckRerolls = 0;
        }

        void OnDestroy()
        {
            // 중복 인스턴스가 늦게 파괴될 때 현재 활성 인스턴스의 상태를 지우지 않도록 소유권을 확인한다
            if (Active == this)
            {
                Active = null;
                LuckRerolls = 0;
            }
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
        ///
        /// 모든 제시는 웨이브 시작 시점에 열린다. 중간 보스 라운드를 넘긴 직후 웨이브(7/13/19)만
        /// 전설 확정 2장 / 다시뽑기 불가이고, 보스 웨이브(6/12/18)를 포함한 나머지는 평시 구성이다.
        /// 보스 처치 순간이 아니라 다음 웨이브 시작에 주는 이유는 제시 시점을 웨이브 시작 하나로 모아
        /// 전투 중에 화면이 멈추지 않게 하려는 것이다.
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

            // 보스 라운드를 넘긴 직후 웨이브(7/13/19)가 보스 클리어 보상이다.
            // 처치 여부는 보지 않는다. 보스를 흘려보내면 이미 라이프 20을 잃고 회복 수단도 없어서,
            // 보상까지 빼면 이중 처벌이 된다.
            // 전설 풀이 바닥나면 OpenOffer가 일반 규칙으로 폴백한다.
            if (_stage.IsBossWave(waveNumber - 1))
            {
                if (!OpenOffer(legendaryOnly: true, _config.BossCardsPerOffer, allowReroll: false))
                    return false;

                _pendingProceed = proceed;

                GameLog.Info("Reward", $"웨이브 {waveNumber} 보스 클리어 보상 제시 (전설 확정)");

                return true;
            }

            if (!OpenOffer(legendaryOnly: false, _config.CardsPerOffer, allowReroll: true))
                return false;

            _pendingProceed = proceed;

            GameLog.Info("Reward", $"웨이브 {waveNumber} 보상 제시");

            return true;
        }

        bool OpenOffer(bool legendaryOnly, int cardCount, bool allowReroll)
        {
            EnsureRerolls();

            _discardedThisOffer.Clear();
            _offerLegendaryOnly = legendaryOnly;
            _offerCardCount = Mathf.Max(1, cardCount);
            _offerAllowReroll = allowReroll;

            // 전설 풀이 바닥났을 때만 제시 전체를 일반 규칙으로 폴백한다 (리롤 중 폴백 금지)
            if (legendaryOnly)
            {
                CollectCandidates(legendaryOnly: true);

                if (_candidateBuffer.Count == 0)
                    _offerLegendaryOnly = false;
            }

            if (!BuildOffer(_offerLegendaryOnly, _offerCardCount))
                return false;

            OfferActive = true;

            // 디밍 동안 게임을 완전히 멈춘다 (스폰 코루틴 포함)
            Time.timeScale = 0f;

            OfferChanged?.Invoke();

            return true;
        }

        /// <summary>다시뽑기 잔여 횟수는 판 전체 공유값이라 최초 1회만 채운다.</summary>
        void EnsureRerolls()
        {
            if (_rerollsInitialized)
                return;

            _rerollsInitialized = true;
            _rerollsLeft = _config.RerollsPerRun;
        }

        bool BuildOffer(bool legendaryOnly, int requestedCount)
        {
            _offer.Clear();

            CollectCandidates(legendaryOnly);

            if (_candidateBuffer.Count == 0)
                return false;

            int cardCount = Mathf.Min(requestedCount, _candidateBuffer.Count);

            for (int i = 0; i < cardCount; i++)
            {
                var picked = PickWeighted(_candidateBuffer, _offer);

                if (picked == null)
                    break;

                _offer.Add(picked);
            }

            return _offer.Count > 0;
        }

        void CollectCandidates(bool legendaryOnly)
        {
            _candidateBuffer.Clear();

            if (_config.Cards == null)
                return;

            foreach (var card in _config.Cards)
            {
                if (card == null || !card.Enabled)
                    continue;

                if (card.Effect == RewardEffectType.None || card.Effect == RewardEffectType.DamageRangeNarrow)
                    continue;

                // 중첩 상한에 도달한 보상은 풀에서 제외된다
                if (StackOf(card) >= card.StackLimit)
                    continue;

                // 이번 제시 화면에서 버린(다시뽑기로 넘긴) 카드는 다시 나오지 않는다
                if (_discardedThisOffer.Contains(card))
                    continue;

                if (legendaryOnly && card.Rarity != RewardRarity.Legendary)
                    continue;

                _candidateBuffer.Add(card);
            }
        }

        /// <summary>
        /// 카드 가중치 = 등급 목표 확률 / 그 등급의 활성 풀 개수.
        /// 카드를 추가/삭제해도 등급별 체감이 유지된다 (스프레드시트: 계산된 배수).
        /// </summary>
        readonly int[] _poolCounts = new int[4];

        RewardDefinition PickWeighted(List<RewardDefinition> candidates, List<RewardDefinition> exclude)
        {
            for (int i = 0; i < _poolCounts.Length; i++)
                _poolCounts[i] = 0;

            if (_config.Cards != null)
            {
                foreach (var card in _config.Cards)
                {
                    if (card == null || !card.Enabled)
                        continue;

                    _poolCounts[(int)card.Rarity]++;
                }
            }

            float total = 0f;

            foreach (var card in candidates)
            {
                if (exclude.Contains(card))
                    continue;

                total += CardWeight(card);
            }

            if (total <= 0f)
                return null;

            float roll = Random.value * total;

            foreach (var card in candidates)
            {
                if (exclude.Contains(card))
                    continue;

                roll -= CardWeight(card);

                if (roll > 0f)
                    continue;

                return card;
            }

            return null;
        }

        float CardWeight(RewardDefinition card)
        {
            int pool = Mathf.Max(1, _poolCounts[(int)card.Rarity]);

            return _config.TargetOf(card.Rarity) / pool;
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
            Version++;

            RecomputeLuck();
            ApplyImmediate(card);

            GameLog.Info("Reward", $"[{card.Id}] {card.DisplayName} 획득 ({count + 1}/{card.StackLimit})");

            CardAcquired?.Invoke(card);

            CloseOffer();
        }

        public bool CanReroll
        {
            get
            {
                if (!OfferActive)
                    return false;

                // 보스 클리어 보상은 다시 뽑기가 없다 (시트: 중간보스 3종)
                if (!_offerAllowReroll)
                    return false;

                if (_rerollsLeft <= 0)
                    return false;

                if (_config.RerollCost > 0 && _stage.Gold < _config.RerollCost)
                    return false;

                // 제시분 전체 교체가 불가능하면(확정 전설 풀 소진 등) 리롤을 막는다
                return RerollCandidateCount() >= _offerCardCount;
            }
        }

        /// <summary>지금 리롤하면 새로 뽑을 수 있는 후보 수 (현재 제시분은 버려지므로 제외).</summary>
        int RerollCandidateCount()
        {
            CollectCandidates(_offerLegendaryOnly);

            int count = 0;

            foreach (var card in _candidateBuffer)
            {
                if (!_offer.Contains(card))
                    count++;
            }

            return count;
        }

        public void Reroll()
        {
            if (!CanReroll)
                return;

            if (_config.RerollCost > 0 && !_stage.TrySpend(_config.RerollCost))
                return;

            _rerollsLeft--;

            // 제시분 전체 교체. 버린 카드는 이 화면에 다시 등장하지 않는다.
            var previous = new List<RewardDefinition>(_offer);

            foreach (var card in previous)
                _discardedThisOffer.Add(card);

            if (!BuildOffer(_offerLegendaryOnly, _offerCardCount))
            {
                // 남은 후보가 없어 교체 불가 - 버린 기록/횟수/비용을 되돌리고 기존 제시 유지
                GameLog.Warn("Reward", "다시뽑기 실패 - 남은 후보 없음");

                foreach (var card in previous)
                    _discardedThisOffer.Remove(card);

                _offer.Clear();
                _offer.AddRange(previous);
                _rerollsLeft++;

                if (_config.RerollCost > 0)
                    _stage.AddGold(_config.RerollCost);

                return;
            }

            GameLog.Info("Reward", $"보상 다시뽑기 (남은 {_rerollsLeft}회)");

            OfferChanged?.Invoke();
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

        /// <summary>제시를 무효화한다 (패배 확정 등). 재개 콜백은 버려진다.</summary>
        public void CancelOffer()
        {
            if (!OfferActive)
                return;

            OfferActive = false;
            _offer.Clear();
            _pendingProceed = null;

            if (_stage != null)
                _stage.ReapplySpeed();

            OfferChanged?.Invoke();
        }

        /// <summary>행운 재굴림이 판정을 뒤집었을 때의 연출. Luck이 호출한다.</summary>
        public static void PlayLuckFx(Vector3 position)
        {
            if (Active == null || Active._luckFx == null)
                return;

            OneShotFx.Spawn(Active._luckFx, position);
        }

        /// <summary>재굴림 횟수를 다시 집계한다 (C15). 매 판정마다 순회하지 않도록 획득 시점에만 부른다.</summary>
        void RecomputeLuck()
        {
            int total = 0;

            ForEachOwned((def, stacks) =>
            {
                if (def.Effect == RewardEffectType.LuckReroll)
                    total += Mathf.RoundToInt(def.Value) * stacks;
            });

            LuckRerolls = Mathf.Max(0, total);
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

        /// <summary>병사 공격이 확률로 적을 밀어낸다 (B1B). 행운 연출은 병사를 낸 병영에 뜬다.</summary>
        public static void TrySoldierKnockback(Monster target, Tower owner)
        {
            if (Active == null || target == null || !target.IsAlive)
                return;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect != RewardEffectType.SoldierKnockbackChance)
                    return;

                if (Luck.Roll(def.Chance, owner))
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
                        if (SourceMatches(def, src) && Luck.Roll(def.Chance, src))
                            target.Knockback(def.Value2);
                        break;

                    case RewardEffectType.StunChance:
                        if (SourceMatches(def, src) && Luck.Roll(def.Chance, src))
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

        /// <summary>
        /// 연발 장전 (C13): 공격할 때마다 확률로 추가 발사체가 나간다.
        /// Chance = 발동 확률, Value = 피해 배율, Value2 = 발수. 중첩 시 확률은 독립 결합.
        /// </summary>
        public static bool TryGetProcShot(TowerType type, out float chance, out int count, out float damageScale)
        {
            chance = 0f;
            count = 0;
            damageScale = 0f;

            if (Active == null)
                return false;

            float keep = 1f;
            int shots = 0;
            float scale = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect != RewardEffectType.BonusProcShot || !def.AppliesTo(type))
                    return;

                for (int i = 0; i < stacks; i++)
                    keep *= 1f - def.Chance;

                shots = Mathf.Max(shots, Mathf.RoundToInt(def.Value2));
                scale = Mathf.Max(scale, def.Value);
            });

            chance = 1f - keep;

            if (chance <= 0f)
                return false;

            count = Mathf.Max(1, shots);
            damageScale = scale;

            return true;
        }

        /// <summary>
        /// 처형 예포 (C14): 처치 시 주변 적으로 추가 발사체가 튄다.
        /// Value = 피해 배율, Value2 = 발수.
        /// </summary>
        public static bool TryGetOnKillShot(TowerType type, out int count, out float damageScale)
        {
            count = 0;
            damageScale = 0f;

            if (Active == null)
                return false;

            int shots = 0;
            float scale = 0f;

            Active.ForEachOwned((def, stacks) =>
            {
                if (def.Effect != RewardEffectType.BonusOnKillShot || !def.AppliesTo(type))
                    return;

                shots = Mathf.Max(shots, Mathf.RoundToInt(def.Value2));
                scale = Mathf.Max(scale, def.Value);
            });

            if (shots <= 0)
                return false;

            count = shots;
            damageScale = scale;

            return true;
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

            return Mathf.RoundToInt(monster.GoldReward * percent + flat);
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
