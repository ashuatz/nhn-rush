using System.Collections.Generic;
using Rush.Data;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 보병 타워가 소환하는 근접 병사. 지상 몬스터를 저지(블로킹)하고 근접전을 벌인다.
    /// 몬스터 궁병의 원거리 표적이 된다. 기획서(코어 룰) 2장 보병 계열.
    ///
    /// 저지 규칙: 표적 예약과 저지를 분리한다. 근접 접촉 전에는 저지하지 않으며,
    /// 접촉한 뒤 매 프레임 저지를 시도하므로 앞선 저지자가 죽어도 즉시 이어받는다.
    /// 보상(B1A)이 있으면 한 병사가 여러 기를 저지할 수 있다.
    /// </summary>
    public class Soldier : MonoBehaviour
    {
        const float MeleeRange = 0.7f;
        const float MoveSpeed = 3f;

        /// <summary>집결지에서 이만큼 벗어난 표적은 놓고 복귀한다 (무한 추격 방지).</summary>
        const float LeashMargin = 2.5f;

        /// <summary>공격할 때 앞으로 찔러 넣는 연출 (비주얼 자식만 움직인다).</summary>
        const float LungeDuration = 0.18f;
        const float LungeDistance = 0.45f;

        static readonly List<Soldier> _active = new List<Soldier>();

        public static IReadOnlyList<Soldier> Active => _active;

        readonly List<Monster> _blocking = new List<Monster>(4);

        InfantryTower _owner;
        float _maxHp;
        float _damage;
        float _attackInterval;
        Vector3 _rallyPoint;
        float _engageRange;

        Monster _target;
        float _attackTimer;

        Transform _visual;
        Vector3 _visualBaseLocal;
        Vector3 _lungeDirection;
        float _lungeTimer;

        public float Hp { get; private set; }

        public bool IsAlive { get; private set; }

        public static void ClearRegistry()
        {
            _active.Clear();
        }

        /// <summary>가장 가까운 살아있는 병사를 찾는다 (몬스터 궁병 표적용).</summary>
        public static Soldier FindNearest(Vector3 origin, float range)
        {
            Soldier best = null;
            float bestSqr = range * range;

            foreach (var soldier in _active)
            {
                if (soldier == null || !soldier.IsAlive)
                    continue;

                float distSqr = (soldier.transform.position - origin).sqrMagnitude;

                if (distSqr > bestSqr)
                    continue;

                best = soldier;
                bestSqr = distSqr;
            }

            return best;
        }

        public void Initialize(InfantryTower owner, float hp, float damage, float attackInterval,
            Vector3 rallyPoint, float engageRange)
        {
            _owner = owner;
            _maxHp = hp;
            _damage = damage;
            _attackInterval = attackInterval;
            _rallyPoint = rallyPoint;
            _engageRange = engageRange;

            Hp = _maxHp;
            IsAlive = true;

            transform.position = rallyPoint;

            _visual = transform.Find("Visual");

            if (_visual != null)
                _visualBaseLocal = _visual.localPosition;

            _active.Add(this);
        }

        void OnDestroy()
        {
            _active.Remove(this);
        }

        void Update()
        {
            if (!IsAlive)
                return;

            TickLunge();

            _blocking.RemoveAll(m => m == null || !m.IsAlive);

            // 표적이 죽었거나 사라졌으면 재탐색으로 넘어간다
            if (_target != null && !_target.IsAlive)
                _target = null;

            if (_target == null)
            {
                AcquireTarget();

                if (_target == null)
                {
                    ReturnToRally();
                    return;
                }
            }

            // 표적이 집결 범위를 크게 벗어나면 놓아주고 복귀한다
            float leash = _engageRange + LeashMargin;

            if ((_target.transform.position - _rallyPoint).sqrMagnitude > leash * leash)
            {
                ClearTarget();
                ReturnToRally();
                return;
            }

            EngageTarget();
        }

        /// <summary>표적 예약만 한다. 저지는 접촉 후 EngageTarget에서 시도한다.</summary>
        void AcquireTarget()
        {
            // 이미 저지 중인 몬스터가 있으면 그쪽을 우선 공격한다
            if (_blocking.Count > 0)
            {
                _target = _blocking[0];
                return;
            }

            _target = MonsterRegistry.FindBlockTarget(_rallyPoint, _engageRange);
        }

        void EngageTarget()
        {
            Vector3 toTarget = _target.transform.position - transform.position;
            toTarget.y = 0f;

            // 근접 사거리 밖이면 접근만 한다 (원거리 저지 금지)
            if (toTarget.magnitude > MeleeRange)
            {
                transform.position += toTarget.normalized * (MoveSpeed * Time.deltaTime);
                return;
            }

            // 접촉 상태에서 매 프레임 저지를 시도한다 - 앞선 저지자가 죽으면 즉시 이어받는다
            if (!_blocking.Contains(_target))
                _target.TryBlock(this);

            _attackTimer -= Time.deltaTime;

            if (_attackTimer > 0f)
                return;

            _attackTimer = _attackInterval;
            _lungeDirection = toTarget.normalized;
            _lungeTimer = LungeDuration;

            var source = DamageSource.FromSoldierUnit(_owner);
            DamageResolver.Apply(_target, _damage, DamageType.Physical, 0f, source);

            // 보상(B1B): 병사 공격이 확률로 적을 밀어내고, 밀린 적은 저지가 풀린다
            RewardSystem.TrySoldierKnockback(_target);
        }

        /// <summary>공격 순간 비주얼만 앞으로 찔렀다가 되돌아온다.</summary>
        void TickLunge()
        {
            if (_visual == null)
                return;

            if (_lungeTimer <= 0f)
                return;

            _lungeTimer -= Time.deltaTime;

            if (_lungeTimer <= 0f)
            {
                _visual.localPosition = _visualBaseLocal;
                return;
            }

            float pulse = Mathf.Sin(Mathf.PI * (1f - _lungeTimer / LungeDuration));

            _visual.localPosition = _visualBaseLocal + _lungeDirection * (LungeDistance * pulse);
        }

        void ReturnToRally()
        {
            Vector3 toRally = _rallyPoint - transform.position;
            toRally.y = 0f;

            if (toRally.magnitude < 0.1f)
                return;

            transform.position += toRally.normalized * (MoveSpeed * Time.deltaTime);
        }

        void ClearTarget()
        {
            if (_target != null && _blocking.Contains(_target))
            {
                _target.ReleaseBlock(this);
                _blocking.Remove(_target);
            }

            _target = null;
        }

        void ReleaseAllBlocks()
        {
            foreach (var monster in _blocking)
            {
                if (monster == null)
                    continue;

                monster.ReleaseBlock(this);
            }

            _blocking.Clear();
            _target = null;
        }

        // ---------- 저지 상태 (Monster와의 상호 통지) ----------

        /// <summary>추가 저지 가능 여부. 기본 1기, 보상(B1A)으로 증가.</summary>
        public bool CanBlockMore()
        {
            if (!IsAlive)
                return false;

            return _blocking.Count < RewardSystem.SoldierMaxBlock();
        }

        /// <summary>Monster.TryBlock 성공 시 몬스터 쪽에서 호출.</summary>
        public void NotifyBlocked(Monster monster)
        {
            if (_blocking.Contains(monster))
                return;

            _blocking.Add(monster);
        }

        /// <summary>저지 중이던 몬스터가 죽거나 이탈했을 때 몬스터 쪽에서 호출.</summary>
        public void NotifyTargetGone(Monster monster)
        {
            _blocking.Remove(monster);

            if (_target == monster)
                _target = null;
        }

        public void TakeDamage(float damage, string sourceLabel)
        {
            if (!IsAlive)
                return;

            // 보상(D03): 병사 피해 감소
            float final = damage * (1f - RewardSystem.SoldierDamageReduction());

            Hp -= final;

            if (GameLog.VerboseCombat)
                GameLog.Info("Dmg", $"{sourceLabel} -> 병사: {final:F0} (남은 체력 {Mathf.Max(0f, Hp):F0})");

            if (Hp > 0f)
                return;

            Die();
        }

        void Die()
        {
            IsAlive = false;

            ReleaseAllBlocks();
            _active.Remove(this);

            if (_owner != null)
                _owner.NotifySoldierDied(this);

            Destroy(gameObject);
        }

        /// <summary>타워 강화/판매 시 소유 타워가 정리할 때 호출.</summary>
        public void DespawnByOwner()
        {
            IsAlive = false;

            ReleaseAllBlocks();
            _active.Remove(this);

            Destroy(gameObject);
        }
    }
}
