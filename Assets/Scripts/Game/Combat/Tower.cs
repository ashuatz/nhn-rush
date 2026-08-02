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

        float _cooldown;
        Transform _visual;

        public TowerData Data { get; private set; }
        public int LevelIndex { get; private set; }
        public int TotalInvested { get; private set; }

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

        protected virtual void Update()
        {
            if (Data == null)
                return;

            _cooldown -= Time.deltaTime;

            if (_cooldown > 0f)
                return;

            if (TryAttack())
                _cooldown = CurrentStat.AttackInterval;
        }

        protected abstract bool TryAttack();

        /// <summary>원거리 계열(궁병/마도/포병) 공용 공격 루틴.</summary>
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

            Vector3 muzzle = transform.position + Vector3.up * 1.2f;
            var go = Instantiate(Data.ProjectilePrefab, muzzle, Quaternion.identity);
            var projectile = go.GetComponent<Projectile>();

            if (projectile == null)
                projectile = go.AddComponent<Projectile>();

            projectile.Launch(target, Data.ProjectileSpeed, stat.Damage, Data.DamageType,
                stat.ArmorPierce, stat.SplashRadius, stat.SlowPercent, stat.SlowDuration,
                stat.DisplayName);

            return true;
        }
    }
}
