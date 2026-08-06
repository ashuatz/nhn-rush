using System.Collections;
using System.Collections.Generic;
using Rush.Data;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>최종 분기 선택 상태. 한 번 고르면 되돌릴 수 없다.</summary>
    public enum TowerBranchChoice
    {
        None = 0,
        A = 1,
        B = 2,
    }

    /// <summary>
    /// 타워 공통 기반. 1~3단계 강화 후 최종 분기(2택)와 분기 스킬(3레벨), 판매, 타겟 탐색.
    /// 실제 사용 수치는 "기본 스탯 x 보상 배율"이며 배율은 RewardSystem에서 읽는다.
    /// 분기 스킬 로직도 여기서 처리한다 (모든 공격이 TryRangedAttack을 지나므로 하위 클래스는 비어 있다).
    /// 스프레드시트(타워 시트/막증축 타워 스킬 정리).
    /// </summary>
    public abstract class Tower : MonoBehaviour
    {
        const string VisualName = "Visual";
        const string TierVisualsName = "TierVisuals";

        /// <summary>판매 환급 기본 70%. 스프레드시트(타워 시트) 공통 규칙.</summary>
        const float BaseSellRefundFraction = 0.7f;

        /// <summary>레벨 수가 티어 모델 수보다 많을 때(Lv4 = 3티어 재사용) 최상위 티어를 키우는 배율.</summary>
        const float ReusedTierScaleStep = 0.12f;

        static readonly List<Tower> _activeTowers = new List<Tower>();
        static readonly List<Monster> _bonusTargets = new List<Monster>(8);

        float _cooldown;
        Transform _visual;
        Transform _tierVisuals;

        // 연속 타격 보상(C08) 상태
        Monster _consecutiveTarget;
        int _consecutiveHits;

        // ---------- 분기 스킬 런타임 상태 ----------
        int[] _skillLevels;
        int _headshotCounter;
        float _burstReadyAt;         // 마개조 기관총: 다음 발동 가능 시각
        float _burstUntil;           // 마개조 기관총: 지속 종료 시각
        float _deathRayReadyAt;      // 죽음의 광선
        float _snapOutReadyAt;       // 정신차려!
        float _clusterReadyAt;       // 집속로켓
        float _dragonBreathReadyAt;  // 용의 숨결
        float _bloodFeastBonus;      // 피의 향연: 다음 기본 공격 보너스 (중첩 없음)

        static readonly List<Monster> _skillTargetBuffer = new List<Monster>(16);

        public TowerData Data { get; private set; }
        public int LevelIndex { get; private set; }
        public int TotalInvested { get; private set; }

        public TowerBranchChoice BranchChoice { get; private set; }

        /// <summary>분기/스킬 구성이 바뀔 때마다 증가. 스탯을 스냅샷한 개체(병사)가 갱신 시점을 감지한다.</summary>
        public int SkillVersion { get; private set; }

        public TowerBranchDef ChosenBranch
        {
            get
            {
                if (BranchChoice == TowerBranchChoice.A)
                    return Data.BranchA;

                if (BranchChoice == TowerBranchChoice.B)
                    return Data.BranchB;

                return null;
            }
        }

        /// <summary>판매 확정 여부. Destroy는 프레임 끝에 반영되므로 그 사이의 추가 발사를 막는다.</summary>
        public bool IsSold { get; private set; }

        protected StageController Stage { get; private set; }

        public TowerLevelStat CurrentStat
        {
            get
            {
                if (LevelIndex < Data.Levels.Length)
                    return Data.Levels[LevelIndex];

                return ChosenBranch.Stat;
            }
        }

        /// <summary>이 타워 기본 공격의 피해 태그.</summary>
        public DamageTag BaseTag
        {
            get
            {
                if (Data.Type == TowerType.Mage)
                    return DamageTag.Magic;

                if (Data.Type == TowerType.Artillery)
                    return DamageTag.Splash;

                return DamageTag.Single;
            }
        }

        /// <summary>보상 배율이 반영된 사거리. 타겟팅과 UI 표시가 함께 쓴다.</summary>
        public float EffectiveRange
        {
            get
            {
                return CurrentStat.Range * RewardSystem.GetStatMods(Data.Type).RangeMul;
            }
        }

        public bool CanUpgrade => LevelIndex < Data.Levels.Length - 1;

        /// <summary>3단계에서 아직 분기를 고르지 않았으면 분기 선택이 가능하다.</summary>
        public bool CanChooseBranch
        {
            get
            {
                if (BranchChoice != TowerBranchChoice.None)
                    return false;

                if (LevelIndex != Data.Levels.Length - 1)
                    return false;

                return Data.BranchA != null && Data.BranchA.IsValid;
            }
        }

        public int UpgradeCost
        {
            get
            {
                if (!CanUpgrade)
                    return 0;

                float mul = RewardSystem.UpgradeCostMultiplier();

                return Mathf.RoundToInt(Data.Levels[LevelIndex + 1].Cost * mul);
            }
        }

        /// <summary>분기 증축 비용. 증축 비용 보상(C03)의 영향을 받는다.</summary>
        public int BranchCost(TowerBranchDef branch)
        {
            if (branch == null || branch.Stat == null)
                return 0;

            float mul = RewardSystem.UpgradeCostMultiplier();

            return Mathf.RoundToInt(branch.Stat.Cost * mul);
        }

        /// <summary>판매 환급: 기본 누적 70%, 보상(C04)으로 상향. 스프레드시트(타워 시트).</summary>
        public int SellRefund
        {
            get
            {
                float fraction = RewardSystem.SellRefundFraction(BaseSellRefundFraction);

                return Mathf.RoundToInt(TotalInvested * fraction);
            }
        }

        /// <summary>발사체가 나가는 위치 (더미 큐브 기준 총구 높이).</summary>
        public Vector3 MuzzlePosition => transform.position + Vector3.up * 1.2f;

        /// <summary>tower의 유효 사거리 안에 있는 다른 타워 수 (A02).</summary>
        public static int CountTowersInRange(Tower tower)
        {
            int count = 0;
            float rangeSqr = tower.EffectiveRange * tower.EffectiveRange;

            foreach (var other in _activeTowers)
            {
                if (other == null || other == tower || other.IsSold)
                    continue;

                if ((other.transform.position - tower.transform.position).sqrMagnitude <= rangeSqr)
                    count++;
            }

            return count;
        }

        public virtual void Initialize(TowerData data, StageController stage)
        {
            Data = data;
            Stage = stage;
            LevelIndex = 0;
            TotalInvested = data.Levels[0].Cost;

            _visual = transform.Find(VisualName);
            _tierVisuals = transform.Find(TierVisualsName);

            _activeTowers.Add(this);

            OnLevelChanged();
        }

        protected virtual void OnDestroy()
        {
            _activeTowers.Remove(this);
        }

        /// <summary>비용 지불은 호출자(BuildMenu)가 StageController.TrySpend로 처리한다. 지불액은 UpgradeCost.</summary>
        public void Upgrade()
        {
            if (!CanUpgrade)
                return;

            int paid = UpgradeCost;

            LevelIndex++;
            TotalInvested += paid;

            OnLevelChanged();

            GameLog.Info("Build", $"{CurrentStat.DisplayName} 강화 완료 (Lv{LevelIndex + 1})");
        }

        // ---------- 최종 분기 / 분기 스킬 ----------

        /// <summary>최종 분기를 확정한다 (되돌릴 수 없음). 비용 지불은 호출자가 처리하며 지불액은 BranchCost.</summary>
        public void ChooseBranch(TowerBranchChoice choice)
        {
            if (!CanChooseBranch)
                return;

            if (choice == TowerBranchChoice.None)
                return;

            BranchChoice = choice;

            var branch = ChosenBranch;

            LevelIndex++;
            TotalInvested += BranchCost(branch);

            int skillCount = branch.Skills != null ? branch.Skills.Length : 0;
            _skillLevels = new int[skillCount];
            SkillVersion++;

            OnLevelChanged();

            GameLog.Info("Build", $"최종 분기 확정: {branch.Name}");
        }

        public int SkillCount
        {
            get
            {
                var branch = ChosenBranch;

                if (branch == null || branch.Skills == null)
                    return 0;

                return branch.Skills.Length;
            }
        }

        public BranchSkillDef GetSkill(int index)
        {
            var branch = ChosenBranch;

            if (branch == null || branch.Skills == null)
                return null;

            if (index < 0 || index >= branch.Skills.Length)
                return null;

            return branch.Skills[index];
        }

        public int GetSkillLevelAt(int index)
        {
            if (_skillLevels == null || index < 0 || index >= _skillLevels.Length)
                return 0;

            return _skillLevels[index];
        }

        /// <summary>다음 레벨 구매 비용. 최대 레벨이면 0.</summary>
        public int SkillUpgradeCost(int index)
        {
            var skill = GetSkill(index);

            if (skill == null)
                return 0;

            int level = GetSkillLevelAt(index);

            if (level >= BranchSkillDef.MaxLevel)
                return 0;

            return skill.CostOfLevel(level);
        }

        /// <summary>스킬 레벨을 1 올린다. 비용 지불은 호출자가 처리하며 지불액은 SkillUpgradeCost.</summary>
        public void UpgradeSkill(int index)
        {
            var skill = GetSkill(index);

            if (skill == null || _skillLevels == null)
                return;

            int level = GetSkillLevelAt(index);

            if (level >= BranchSkillDef.MaxLevel)
                return;

            int paid = SkillUpgradeCost(index);

            _skillLevels[index]++;
            TotalInvested += paid;
            SkillVersion++;

            GameLog.Info("Build", $"{skill.DisplayName} Lv{_skillLevels[index]} 구매");
        }

        /// <summary>해당 효과의 보유 레벨 (0=미보유). 병사 등 외부 개체도 조회한다.</summary>
        public int SkillLevel(BranchSkillType type)
        {
            var branch = ChosenBranch;

            if (branch == null || branch.Skills == null || _skillLevels == null)
                return 0;

            for (int i = 0; i < branch.Skills.Length; i++)
            {
                if (branch.Skills[i] != null && branch.Skills[i].Type == type)
                    return _skillLevels[i];
            }

            return 0;
        }

        /// <summary>효과 정의와 보유 레벨을 함께 얻는다. 미보유면 false.</summary>
        public bool TryGetSkill(BranchSkillType type, out BranchSkillDef def, out int level)
        {
            def = null;
            level = 0;

            var branch = ChosenBranch;

            if (branch == null || branch.Skills == null || _skillLevels == null)
                return false;

            for (int i = 0; i < branch.Skills.Length; i++)
            {
                var skill = branch.Skills[i];

                if (skill == null || skill.Type != type)
                    continue;

                if (_skillLevels[i] <= 0)
                    return false;

                def = skill;
                level = _skillLevels[i];

                return true;
            }

            return false;
        }

        /// <summary>처치 귀속 통지 (Projectile이 호출). 피의 향연이 다음 공격 보너스를 쌓는다.</summary>
        public void NotifyKillByThisTower()
        {
            if (TryGetSkill(BranchSkillType.BloodFeast, out var feast, out int level))
                _bloodFeastBonus = feast.ValueAt(level);
        }

        /// <summary>판매 직전에 호출한다. 남은 코루틴을 끊고 이후 발사 요청을 전부 무시한다.</summary>
        public void MarkSold()
        {
            IsSold = true;

            StopAllCoroutines();
        }

        /// <summary>
        /// 레벨 변화 시 비주얼 갱신.
        /// 티어 모델(TierVisuals 자식)이 있으면 레벨에 맞는 티어만 켠다.
        /// 레벨이 티어 수를 넘으면(Lv4 = 3티어) 최상위 티어를 재사용하고 살짝 키워 구분한다.
        /// 티어 모델이 없으면 예전 방식(더미 큐브 스케일)으로 폴백한다.
        /// </summary>
        protected virtual void OnLevelChanged()
        {
            if (_tierVisuals != null && _tierVisuals.childCount > 0)
            {
                ApplyTierVisual();
                return;
            }

            if (_visual == null)
                return;

            float scale = 1f + LevelIndex * 0.15f;
            _visual.localScale = new Vector3(scale, scale, scale);
        }

        void ApplyTierVisual()
        {
            // 더미 큐브는 티어 모델이 있으면 항상 끈다
            if (_visual != null)
                _visual.gameObject.SetActive(false);

            int tierCount = _tierVisuals.childCount;
            int tierIndex = Mathf.Min(LevelIndex, tierCount - 1);
            int reusedSteps = Mathf.Max(0, LevelIndex - (tierCount - 1));

            for (int i = 0; i < tierCount; i++)
            {
                var tier = _tierVisuals.GetChild(i);
                bool active = i == tierIndex;

                tier.gameObject.SetActive(active);

                if (!active)
                    continue;

                float scale = 1f + reusedSteps * ReusedTierScaleStep;
                tier.localScale = new Vector3(scale, scale, scale);
            }
        }

        protected virtual void Update()
        {
            if (Data == null)
                return;

            if (IsSold)
                return;

            TickSnapOut();

            _cooldown -= Time.deltaTime;

            if (_cooldown > 0f)
                return;

            if (TryAttack())
            {
                float speedMul = RewardSystem.GetStatMods(Data.Type).AttackSpeedMul;
                float interval = CurrentStat.AttackInterval * BurstIntervalMultiplier();

                _cooldown = interval / Mathf.Max(0.1f, speedMul);
            }
        }

        /// <summary>마개조 기관총: 15초마다 지속 시간 동안 공격주기 80% 감소. 발동 시각 기준 15초 주기.</summary>
        float BurstIntervalMultiplier()
        {
            if (!TryGetSkill(BranchSkillType.MachineGunBurst, out var skill, out int level))
                return 1f;

            // 구매 직후 첫 주기는 15초를 채운 뒤 발동한다
            if (_burstReadyAt <= 0f)
            {
                _burstReadyAt = Time.time + 15f;
                return 1f;
            }

            if (Time.time < _burstUntil)
                return 0.2f;

            if (Time.time < _burstReadyAt)
                return 1f;

            _burstUntil = Time.time + skill.ValueAt(level);
            _burstReadyAt = Time.time + 15f;

            GameLog.Info("Skill", $"{skill.DisplayName} 발동 ({skill.ValueAt(level):0.#}초)");

            return 0.2f;
        }

        /// <summary>정신차려!: 30초마다 사거리 내 N명이 3초간 서로를 공격한다 (마법사의 공격으로 간주).</summary>
        void TickSnapOut()
        {
            if (!TryGetSkill(BranchSkillType.SnapOut, out var skill, out int level))
                return;

            if (_snapOutReadyAt <= 0f)
            {
                _snapOutReadyAt = Time.time + 30f;
                return;
            }

            if (Time.time < _snapOutReadyAt)
                return;

            int count = Mathf.RoundToInt(skill.ValueAt(level));

            MonsterRegistry.CollectNearest(transform.position, EffectiveRange, Data.CanTargetFlying,
                count, null, _skillTargetBuffer);

            if (_skillTargetBuffer.Count < 2)
                return;

            _snapOutReadyAt = Time.time + 30f;

            StartCoroutine(RunSnapOut(new List<Monster>(_skillTargetBuffer), skill.DisplayName));
        }

        IEnumerator RunSnapOut(List<Monster> targets, string label)
        {
            var source = DamageSource.FromTower(this, DamageTag.Magic, label);
            var wait = new WaitForSeconds(1f);

            GameLog.Info("Skill", $"{label} 발동 ({targets.Count}명)");

            for (int tick = 0; tick < 3; tick++)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var victim = targets[i];

                    if (victim == null || !victim.IsAlive)
                        continue;

                    // 다른 대상 하나가 이 대상을 공격한다. 적의 공격은 마법사의 공격으로 간주한다.
                    var attacker = targets[(i + 1) % targets.Count];

                    if (attacker == null || !attacker.IsAlive || attacker == victim)
                        continue;

                    float damage = attacker.ScaledAttackDamage;

                    if (damage <= 0f)
                        continue;

                    DamageResolver.Apply(victim, damage, DamageType.Magical, 0f, source);
                }

                if (tick < 2)
                    yield return wait;
            }
        }

        protected abstract bool TryAttack();

        // ---------- 기본 공격 ----------

        /// <summary>원거리 계열(궁병/마도/포병) 공용 공격 루틴. 연출 설정에 따라 단발 또는 연발.</summary>
        protected bool TryRangedAttack()
        {
            var stat = CurrentStat;
            var target = MonsterRegistry.FindTarget(transform.position, EffectiveRange, Data.CanTargetFlying);

            if (target == null)
                return false;

            if (Data.ProjectilePrefab == null)
            {
                GameLog.Warn("Build", $"{Data.name}: 발사체 프리팹이 비어 있음");
                return false;
            }

            // 죽음의 광선: 20초마다 다음 기본 공격이 사거리 내 체력 최다 적에게 마법 피해
            if (TryCastDeathRay(stat))
                return true;

            // 집속로켓: 12초마다 다음 기본 공격이 무작위 적에게 동시 발사 + 1초 기절
            if (TryCastClusterRocket(stat))
                return true;

            int shots = 1;

            if (Data.Motion != null && Data.Motion.ShotCount > 1)
                shots = Data.Motion.ShotCount;

            if (shots == 1)
            {
                FireOne(target, stat, stat.Damage);
            }
            else
            {
                // 연발은 피해를 균등 분배해 DPS를 유지한다
                StartCoroutine(FireVolley(stat, shots));
            }

            // 확률 판정은 발사체 수와 무관하게 "공격 1회"당 한 번만 한다
            TryProcShot(target, stat);

            return true;
        }

        bool TryCastDeathRay(TowerLevelStat stat)
        {
            if (!TryGetSkill(BranchSkillType.DeathRay, out var skill, out int level))
                return false;

            if (_deathRayReadyAt <= 0f)
            {
                _deathRayReadyAt = Time.time + 20f;
                return false;
            }

            if (Time.time < _deathRayReadyAt)
                return false;

            // 사거리 내 체력이 가장 많은 적
            MonsterRegistry.CollectInRange(transform.position, EffectiveRange, Data.CanTargetFlying, _skillTargetBuffer);

            Monster best = null;

            foreach (var monster in _skillTargetBuffer)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                if (best == null || monster.Hp > best.Hp)
                    best = monster;
            }

            if (best == null)
                return false;

            _deathRayReadyAt = Time.time + 20f;

            float damage = skill.ValueAt(level) * ConsumeBloodFeast();

            var source = DamageSource.FromTower(this, DamageTag.Magic, skill.DisplayName);
            DamageResolver.Apply(best, damage, DamageType.Magical, 0f, source);

            if (best != null && !best.IsAlive)
                NotifyKillByThisTower();

            GameLog.Info("Skill", $"{skill.DisplayName} 발동 ({damage:F0})");

            return true;
        }

        bool TryCastClusterRocket(TowerLevelStat stat)
        {
            if (!TryGetSkill(BranchSkillType.ClusterRocket, out var skill, out int level))
                return false;

            if (_clusterReadyAt <= 0f)
            {
                _clusterReadyAt = Time.time + 12f;
                return false;
            }

            if (Time.time < _clusterReadyAt)
                return false;

            MonsterRegistry.CollectInRange(transform.position, EffectiveRange, Data.CanTargetFlying, _skillTargetBuffer);

            if (_skillTargetBuffer.Count == 0)
                return false;

            _clusterReadyAt = Time.time + 12f;

            var mods = RewardSystem.GetStatMods(Data.Type);
            int rockets = Mathf.RoundToInt(skill.ValueAt(level));

            var config = new ProjectileConfig
            {
                Motion = Data.Motion,
                ImpactPrefab = Data.ImpactPrefab,
                DamageType = Data.DamageType,
                Speed = Data.ProjectileSpeed,
                Damage = stat.Damage * mods.DamageMul,
                ArmorPierce = stat.ArmorPierce,
                SplashRadius = stat.SplashRadius * mods.SplashMul,
                StunDuration = 1f,
                Source = DamageSource.FromTower(this, BaseTag, skill.DisplayName),
                BonusOwner = this,
            };

            for (int i = 0; i < rockets; i++)
            {
                var target = _skillTargetBuffer[Random.Range(0, _skillTargetBuffer.Count)];

                if (target == null || !target.IsAlive)
                    continue;

                Spawn(Data.ProjectilePrefab, config, target, MuzzlePosition);
            }

            GameLog.Info("Skill", $"{skill.DisplayName} 발동 ({rockets}발)");

            return true;
        }

        /// <summary>피의 향연 보너스를 소비한다 (중첩 없음, 다음 기본 공격 1회).</summary>
        float ConsumeBloodFeast()
        {
            float mul = 1f + _bloodFeastBonus;

            _bloodFeastBonus = 0f;

            return mul;
        }

        IEnumerator FireVolley(TowerLevelStat stat, int shots)
        {
            float perShot = stat.Damage / shots;
            var wait = new WaitForSeconds(Data.Motion.ShotInterval);

            for (int i = 0; i < shots; i++)
            {
                var target = MonsterRegistry.FindTarget(transform.position, EffectiveRange, Data.CanTargetFlying);

                if (target == null)
                    yield break;

                FireOne(target, stat, perShot);

                if (i < shots - 1)
                    yield return wait;
            }
        }

        void FireOne(Monster target, TowerLevelStat stat, float baseDamage)
        {
            var mods = RewardSystem.GetStatMods(Data.Type);

            // 피해 = 기본 x 타워 배율 x 발사 시점 조건부 배율 x 분기 스킬 배율
            float damage = baseDamage * mods.DamageMul * RewardSystem.FireTimeDamageMultiplier(this, target);

            damage *= ConsumeBloodFeast();
            damage *= BranchDamageMultiplier(target);

            // 슬로우: 기본값에 보상 가산치를 더하고 지속시간 배율을 곱한다
            float slowPercent = stat.SlowPercent + mods.SlowPercentAdd;
            float slowDuration = stat.SlowDuration;

            if (stat.SlowPercent <= 0f && mods.SlowPercentAdd > 0f)
                slowDuration = mods.SlowAddDuration;

            slowDuration *= mods.SlowDurationMul;

            float splashRadius = stat.SplashRadius * mods.SplashMul;

            // 용암포탄: 광역 범위 증가 + 맞은 적 20% 둔화
            if (TryGetSkill(BranchSkillType.LavaShell, out var lava, out int lavaLevel))
            {
                splashRadius *= 1f + lava.ValueAt(lavaLevel);

                if (slowPercent < 0.2f)
                {
                    slowPercent = 0.2f;
                    slowDuration = Mathf.Max(slowDuration, 1f);
                }
            }

            var config = new ProjectileConfig
            {
                Motion = Data.Motion,
                ImpactPrefab = Data.ImpactPrefab,
                DamageType = Data.DamageType,
                Speed = Data.ProjectileSpeed,
                Damage = damage,
                ArmorPierce = stat.ArmorPierce,
                SplashRadius = splashRadius,
                SlowPercent = slowPercent,
                SlowDuration = slowDuration,
                Source = DamageSource.FromTower(this, BaseTag, stat.DisplayName),
                BonusOwner = this,
            };

            ApplyBranchRiders(ref config);

            // 헤드샷: 공격 60번마다 추가 물리 피해 + 일반 몬스터 즉사 확률
            if (TryGetSkill(BranchSkillType.Headshot, out var headshot, out int headshotLevel))
            {
                _headshotCounter++;

                if (_headshotCounter >= 60)
                {
                    _headshotCounter = 0;
                    config.Damage += headshot.ValueAt(headshotLevel);
                    config.InstantKillChance = headshot.ChanceAt(headshotLevel);

                    GameLog.Info("Skill", $"{headshot.DisplayName} 발동");
                }
            }

            // 융단 폭격(B2B): 광역 공격이 두 발로 나뉘어 각각 다른 지점에 떨어진다
            float split = RewardSystem.SplitShotFraction(Data.Type);

            if (split > 0f && config.SplashRadius > 0f)
            {
                var splitConfig = config;
                splitConfig.Damage = damage * split;

                Spawn(Data.ProjectilePrefab, splitConfig, target, MuzzlePosition);
                Spawn(Data.ProjectilePrefab, splitConfig, target, MuzzlePosition);
                return;
            }

            Spawn(Data.ProjectilePrefab, config, target, MuzzlePosition);
        }

        /// <summary>발사 시점 분기 스킬 피해 배율: 크리티컬(2배) + 활강포탄(거리 비례).</summary>
        float BranchDamageMultiplier(Monster target)
        {
            float mul = 1f;

            // 정확한 조준: 크리티컬 확률, 크리티컬은 2배 피해
            if (TryGetSkill(BranchSkillType.CriticalAim, out var crit, out int critLevel))
            {
                if (Random.value < crit.ChanceAt(critLevel))
                    mul *= 2f;
            }

            // 활강포탄: 먼 거리의 적일수록 피해 증가 (사거리 대비 비율)
            if (TryGetSkill(BranchSkillType.GlideShell, out var glide, out int glideLevel) && target != null)
            {
                float range = Mathf.Max(0.1f, EffectiveRange);
                float distance = Vector3.Distance(transform.position, target.transform.position);

                mul *= 1f + glide.ValueAt(glideLevel) * Mathf.Clamp01(distance / range);
            }

            return mul;
        }

        /// <summary>착탄 부가 효과를 설정한다: 급조 철갑탄(방깎), 길잃은 방랑자(귀환), 용의 숨결(화염 장판).</summary>
        void ApplyBranchRiders(ref ProjectileConfig config)
        {
            if (TryGetSkill(BranchSkillType.ArmorShredShot, out var shred, out int shredLevel))
                config.PhysShredChance = shred.ChanceAt(shredLevel);

            if (TryGetSkill(BranchSkillType.LostWanderer, out var wander, out int wanderLevel))
                config.TeleportChance = wander.ChanceAt(wanderLevel);

            // 용의 숨결: 6초마다 다음 공격이 3초간 매초 피해를 주는 화염 장판을 남긴다
            if (TryGetSkill(BranchSkillType.DragonBreath, out var breath, out int breathLevel))
            {
                if (_dragonBreathReadyAt <= 0f)
                {
                    _dragonBreathReadyAt = Time.time + 6f;
                }
                else if (Time.time >= _dragonBreathReadyAt)
                {
                    _dragonBreathReadyAt = Time.time + 6f;

                    config.GroundFireDps = breath.ValueAt(breathLevel);
                    config.GroundFireSeconds = 3f;
                    config.GroundFireRadius = Mathf.Max(1f, config.SplashRadius);
                }
            }
        }

        /// <summary>
        /// 연속 타격 보상(C08). 같은 표적에 쏠 때마다 스택이 쌓이고, 표적이 바뀌면 초기화된다.
        /// 반환값은 이번 발사에 적용할 보너스 (이전까지의 스택 기준).
        /// </summary>
        public float ConsecutiveHitBonus(Monster target, float perStack, float cap)
        {
            if (target != _consecutiveTarget)
            {
                _consecutiveTarget = target;
                _consecutiveHits = 0;
            }

            float bonus = Mathf.Min(_consecutiveHits * perStack, cap);
            _consecutiveHits++;

            return bonus;
        }

        // ---------- 추가 발사 ----------

        /// <summary>공격할 때마다 확률로 추가 발사체를 쏜다.</summary>
        void TryProcShot(Monster target, TowerLevelStat stat)
        {
            if (IsSold)
                return;

            var extras = Data.Extras;

            if (extras == null || !extras.ProcEnabled)
                return;

            if (extras.ProcPrefab == null)
            {
                GameLog.Warn("Build", $"{Data.name}: 확률 발사 프리팹이 비어 있음");
                return;
            }

            if (Random.value > extras.ProcChance)
                return;

            var mods = RewardSystem.GetStatMods(Data.Type);

            var config = new ProjectileConfig
            {
                Motion = extras.ProcMotion,
                ImpactPrefab = extras.ProcImpactPrefab,
                DamageType = Data.DamageType,
                Speed = extras.ProcSpeed,
                Damage = stat.Damage * mods.DamageMul * extras.ProcDamageScale,
                ArmorPierce = stat.ArmorPierce,
                SplashRadius = extras.ProcSplashRadius * mods.SplashMul,
                SlowPercent = 0f,
                SlowDuration = 0f,
                Source = DamageSource.FromTower(this, DamageTag.Splash, $"{stat.DisplayName} 추가탄"),
                BonusOwner = null,
            };

            for (int i = 0; i < Mathf.Max(1, extras.ProcCount); i++)
                Spawn(extras.ProcPrefab, config, target, MuzzlePosition);

            if (GameLog.VerboseCombat)
                GameLog.Info("Proc", $"{stat.DisplayName} 확률 발사 ({extras.ProcChance:P0})");
        }

        /// <summary>이 타워가 적을 죽였을 때 주변 적으로 튀는 발사체. Projectile이 호출한다.</summary>
        public void FireOnKillShots(Vector3 origin, Monster killed)
        {
            if (IsSold)
                return;

            var extras = Data.Extras;

            if (extras == null || !extras.OnKillEnabled)
                return;

            if (extras.OnKillPrefab == null)
            {
                GameLog.Warn("Build", $"{Data.name}: 처치 시 발사 프리팹이 비어 있음");
                return;
            }

            MonsterRegistry.CollectNearest(origin, extras.OnKillSearchRadius, Data.CanTargetFlying,
                extras.OnKillCount, killed, _bonusTargets);

            if (_bonusTargets.Count == 0)
                return;

            var stat = CurrentStat;
            var mods = RewardSystem.GetStatMods(Data.Type);

            var config = new ProjectileConfig
            {
                Motion = extras.OnKillMotion,
                ImpactPrefab = extras.OnKillImpactPrefab,
                DamageType = Data.DamageType,
                Speed = extras.OnKillSpeed,
                Damage = stat.Damage * mods.DamageMul * extras.OnKillDamageScale,
                ArmorPierce = stat.ArmorPierce,
                SplashRadius = 0f,
                SlowPercent = 0f,
                SlowDuration = 0f,
                Source = DamageSource.FromTower(this, DamageTag.Single, $"{stat.DisplayName} 추격탄"),
                BonusOwner = null,
            };

            int count = Mathf.Max(1, extras.OnKillCount);

            for (int i = 0; i < count; i++)
            {
                var target = _bonusTargets[i % _bonusTargets.Count];

                Spawn(extras.OnKillPrefab, config, target, origin + Vector3.up * 0.5f);
            }

            if (GameLog.VerboseCombat)
                GameLog.Info("Proc", $"{stat.DisplayName} 처치 시 발사 {count}발");
        }

        void Spawn(GameObject prefab, in ProjectileConfig config, Monster target, Vector3 origin)
        {
            var go = Instantiate(prefab, origin, Quaternion.identity);
            var projectile = go.GetComponent<Projectile>();

            if (projectile == null)
                projectile = go.AddComponent<Projectile>();

            projectile.Launch(config, target, origin);
        }
    }
}
