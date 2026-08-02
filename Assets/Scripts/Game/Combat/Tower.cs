using System.Collections;
using System.Collections.Generic;
using Rush.Data;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 타워 공통 기반. 레벨(Lv1~4 선형 강화), 업그레이드/판매(누적 90% 환급), 타겟 탐색.
    /// 계열별 공격 동작은 하위 클래스가 TryAttack으로 구현한다. 기획서(코어 룰) 2장.
    /// </summary>
    public abstract class Tower : MonoBehaviour
    {
        const string VisualName = "Visual";

        static readonly List<Monster> _bonusTargets = new List<Monster>(8);

        float _cooldown;
        Transform _visual;

        public TowerData Data { get; private set; }
        public int LevelIndex { get; private set; }
        public int TotalInvested { get; private set; }

        /// <summary>판매 확정 여부. Destroy는 프레임 끝에 반영되므로 그 사이의 추가 발사를 막는다.</summary>
        public bool IsSold { get; private set; }

        protected StageController Stage { get; private set; }

        public TowerLevelStat CurrentStat => Data.Levels[LevelIndex];

        public bool CanUpgrade => LevelIndex < Data.Levels.Length - 1;

        public int UpgradeCost
        {
            get
            {
                if (!CanUpgrade)
                    return 0;

                return Data.Levels[LevelIndex + 1].Cost;
            }
        }

        /// <summary>판매 환급: 누적 투자 비용의 90%. 기획서(코어 룰) 1.2.</summary>
        public int SellRefund => Mathf.RoundToInt(TotalInvested * 0.9f);

        /// <summary>발사체가 나가는 위치 (더미 큐브 기준 총구 높이).</summary>
        public Vector3 MuzzlePosition => transform.position + Vector3.up * 1.2f;

        public virtual void Initialize(TowerData data, StageController stage)
        {
            Data = data;
            Stage = stage;
            LevelIndex = 0;
            TotalInvested = data.Levels[0].Cost;

            _visual = transform.Find(VisualName);

            OnLevelChanged();
        }

        /// <summary>비용 지불은 호출자(BuildMenu)가 StageController.TrySpend로 처리한다.</summary>
        public void Upgrade()
        {
            if (!CanUpgrade)
                return;

            LevelIndex++;
            TotalInvested += CurrentStat.Cost;

            OnLevelChanged();

            GameLog.Info("Build", $"{CurrentStat.DisplayName} 강화 완료 (Lv{LevelIndex + 1})");
        }

        /// <summary>레벨 변화 시 더미 비주얼 스케일로 단계를 표현한다 (리소스 교체 전까지).</summary>
        protected virtual void OnLevelChanged()
        {
            if (_visual == null)
                return;

            float scale = 1f + LevelIndex * 0.15f;
            _visual.localScale = new Vector3(scale, scale, scale);
        }

        /// <summary>판매 직전에 호출한다. 남은 코루틴을 끊고 이후 발사 요청을 전부 무시한다.</summary>
        public void MarkSold()
        {
            IsSold = true;

            StopAllCoroutines();
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
                _cooldown = CurrentStat.AttackInterval;
        }

        protected abstract bool TryAttack();

        // ---------- 기본 공격 ----------

        /// <summary>원거리 계열(궁병/마도/포병) 공용 공격 루틴. 연출 설정에 따라 단발 또는 연발.</summary>
        protected bool TryRangedAttack()
        {
            var stat = CurrentStat;
            var target = MonsterRegistry.FindTarget(transform.position, stat.Range, Data.CanTargetFlying);

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
                var target = MonsterRegistry.FindTarget(transform.position, stat.Range, Data.CanTargetFlying);

                if (target == null)
                    yield break;

                FireOne(target, stat, perShot);

                if (i < shots - 1)
                    yield return wait;
            }
        }

        void FireOne(Monster target, TowerLevelStat stat, float damage)
        {
            var config = new ProjectileConfig
            {
                Motion = Data.Motion,
                ImpactPrefab = Data.ImpactPrefab,
                DamageType = Data.DamageType,
                Speed = Data.ProjectileSpeed,
                Damage = damage,
                ArmorPierce = stat.ArmorPierce,
                SplashRadius = stat.SplashRadius,
                SlowPercent = stat.SlowPercent,
                SlowDuration = stat.SlowDuration,
                SourceLabel = stat.DisplayName,
                BonusOwner = this,
            };

            Spawn(Data.ProjectilePrefab, config, target, MuzzlePosition);
        }

        // ---------- 추가 발사 (개발자 실험용) ----------

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

            var config = new ProjectileConfig
            {
                Motion = extras.ProcMotion,
                ImpactPrefab = extras.ProcImpactPrefab,
                DamageType = Data.DamageType,
                Speed = extras.ProcSpeed,
                Damage = stat.Damage * extras.ProcDamageScale,
                ArmorPierce = stat.ArmorPierce,
                SplashRadius = extras.ProcSplashRadius,
                SlowPercent = 0f,
                SlowDuration = 0f,
                SourceLabel = $"{stat.DisplayName} 추가탄",
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

            var config = new ProjectileConfig
            {
                Motion = extras.OnKillMotion,
                ImpactPrefab = extras.OnKillImpactPrefab,
                DamageType = Data.DamageType,
                Speed = extras.OnKillSpeed,
                Damage = stat.Damage * extras.OnKillDamageScale,
                ArmorPierce = stat.ArmorPierce,
                SplashRadius = 0f,
                SlowPercent = 0f,
                SlowDuration = 0f,
                SourceLabel = $"{stat.DisplayName} 추격탄",
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
