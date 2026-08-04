using System.Collections;
using System.Collections.Generic;
using Rush.Data;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 타워 공통 기반. 레벨(Lv1~4 선형 강화), 업그레이드/판매, 타겟 탐색.
    /// 실제 사용 수치는 "기본 스탯 x 보상 배율"이며 배율은 RewardSystem에서 읽는다.
    /// 계열별 공격 동작은 하위 클래스가 TryAttack으로 구현한다. 기획서(코어 룰) 2장.
    /// </summary>
    public abstract class Tower : MonoBehaviour
    {
        const string VisualName = "Visual";
        const float BaseSellRefundFraction = 0.9f;

        static readonly List<Tower> _activeTowers = new List<Tower>();
        static readonly List<Monster> _bonusTargets = new List<Monster>(8);

        float _cooldown;
        Transform _visual;

        // 연속 타격 보상(C08) 상태
        Monster _consecutiveTarget;
        int _consecutiveHits;

        public TowerData Data { get; private set; }
        public int LevelIndex { get; private set; }
        public int TotalInvested { get; private set; }

        /// <summary>판매 확정 여부. Destroy는 프레임 끝에 반영되므로 그 사이의 추가 발사를 막는다.</summary>
        public bool IsSold { get; private set; }

        protected StageController Stage { get; private set; }

        public TowerLevelStat CurrentStat => Data.Levels[LevelIndex];

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

        /// <summary>판매 환급: 기본 누적 90%, 보상(C04)으로 상향. 기획서(코어 룰) 1.2.</summary>
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

        /// <summary>판매 직전에 호출한다. 남은 코루틴을 끊고 이후 발사 요청을 전부 무시한다.</summary>
        public void MarkSold()
        {
            IsSold = true;

            StopAllCoroutines();
        }

        /// <summary>레벨 변화 시 더미 비주얼 스케일로 단계를 표현한다 (리소스 교체 전까지).</summary>
        protected virtual void OnLevelChanged()
        {
            if (_visual == null)
                return;

            float scale = 1f + LevelIndex * 0.15f;
            _visual.localScale = new Vector3(scale, scale, scale);
        }

        protected virtual void Update()
        {
            if (Data == null)
                return;

            if (IsSold)
                return;

            _cooldown -= Time.deltaTime;

            if (_cooldown > 0f)
                return;

            if (TryAttack())
            {
                float speedMul = RewardSystem.GetStatMods(Data.Type).AttackSpeedMul;

                _cooldown = CurrentStat.AttackInterval / Mathf.Max(0.1f, speedMul);
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

            // 피해 = 기본 x 타워 배율 x 발사 시점 조건부 배율
            float damage = baseDamage * mods.DamageMul * RewardSystem.FireTimeDamageMultiplier(this, target);

            // 슬로우: 기본값에 보상 가산치를 더하고 지속시간 배율을 곱한다
            float slowPercent = stat.SlowPercent + mods.SlowPercentAdd;
            float slowDuration = stat.SlowDuration;

            if (stat.SlowPercent <= 0f && mods.SlowPercentAdd > 0f)
                slowDuration = mods.SlowAddDuration;

            slowDuration *= mods.SlowDurationMul;

            var config = new ProjectileConfig
            {
                Motion = Data.Motion,
                ImpactPrefab = Data.ImpactPrefab,
                DamageType = Data.DamageType,
                Speed = Data.ProjectileSpeed,
                Damage = damage,
                ArmorPierce = stat.ArmorPierce,
                SplashRadius = stat.SplashRadius * mods.SplashMul,
                SlowPercent = slowPercent,
                SlowDuration = slowDuration,
                Source = DamageSource.FromTower(this, BaseTag, stat.DisplayName),
                BonusOwner = this,
            };

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
