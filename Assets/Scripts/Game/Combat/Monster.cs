using System;
using Rush.Data;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 몬스터 개체. 경로를 "진행 거리" 기준으로 이동하고(넉백/타겟 우선순위가 정확해진다),
    /// 체력/방어, 병사 저지 대응, 마법 재생, 원거리(궁병형) 공격을 처리한다.
    /// 방어등급은 스폰 시 런타임 단계(0=없음 ~ 5=면역)로 복사되어 보상이 영구적으로 낮출 수 있다.
    /// 죽음/출구 도달은 Initialize에서 받은 콜백으로 StageController에 보고한다.
    /// </summary>
    public class Monster : MonoBehaviour
    {
        const float FlyHeight = 2.2f;
        const float GroundHeight = 0f;

        /// <summary>데이터 오류(0 이하 이동속도)로 경로가 멈추는 것을 막는 최소 속도.</summary>
        const float MinMoveSpeed = 0.5f;

        Action<Monster> _onDied;
        Action<Monster> _onReachedExit;
        PathRoute _route;
        float _distance;
        float _yOffset;
        float _moveSpeed;

        float _slowPercent;
        float _slowUntil;
        float _stunUntil;
        float _stunImmuneUntil;

        Soldier _blockedBy;
        float _meleeTimer;
        float _rangedTimer;

        public MonsterData Data { get; private set; }
        public float Hp { get; private set; }
        public float MaxHp { get; private set; }

        /// <summary>경로 진행 거리 (정확한 값). 타워 타겟팅 우선순위에 쓴다.</summary>
        public float PathProgress => _distance;

        public bool IsAlive { get; private set; }

        /// <summary>현재 병사에게 저지당하고 있는지.</summary>
        public bool IsBlocked => _blockedBy != null;

        public bool IsSlowed => Time.time < _slowUntil;

        public bool IsStunned => Time.time < _stunUntil;

        /// <summary>감속 또는 저지 상태 (통제 계열 보상의 공통 조건).</summary>
        public bool IsControlled
        {
            get
            {
                if (IsBlocked)
                    return true;

                return IsSlowed;
            }
        }

        /// <summary>물리 방어 런타임 단계. 0=없음, 1~4=낮음~매우높음, 5=면역.</summary>
        public int PhysStage { get; private set; }

        /// <summary>마법 저항 런타임 단계. 보상으로 영구 하락 가능.</summary>
        public int MagicStage { get; private set; }

        /// <summary>마지막으로 피해를 준 출처. 처치 귀속(막타 골드 등)에 쓴다.</summary>
        public DamageSource LastHitSource { get; private set; }

        /// <summary>마지막 피격 시점에 통제(감속/저지) 상태였는지. 사망 시 저지가 풀려도 판정이 남는다.</summary>
        public bool ControlledAtLastHit { get; private set; }

        public void Initialize(MonsterData data, PathRoute route, float hpMultiplier,
            Action<Monster> onDied, Action<Monster> onReachedExit)
        {
            Data = data;
            _route = route;
            _onDied = onDied;
            _onReachedExit = onReachedExit;

            MaxHp = data.MaxHp * hpMultiplier;
            Hp = MaxHp;
            IsAlive = true;

            PhysStage = (int)data.PhysicalDefense + 1;
            MagicStage = (int)data.MagicalDefense + 1;

            // 이동속도가 0 이하면 출구에도 못 가고 죽지도 않아 승패 판정이 막힌다
            _moveSpeed = data.MoveSpeed;

            if (_moveSpeed <= 0f)
            {
                GameLog.Warn("Wave", $"{data.DisplayName}: MoveSpeed가 {data.MoveSpeed} - 최소값 {MinMoveSpeed}로 보정");
                _moveSpeed = MinMoveSpeed;
            }

            if (data.IsFlying)
                _yOffset = FlyHeight;
            else
                _yOffset = GroundHeight;

            _distance = 0f;
            transform.position = _route.GetPositionAtDistance(0f) + Vector3.up * _yOffset;

            MonsterRegistry.Register(this);
        }

        void OnDestroy()
        {
            MonsterRegistry.Unregister(this);
        }

        void Update()
        {
            if (!IsAlive)
                return;

            TickRegen();

            if (IsStunned)
                return;

            TickRangedAttack();

            // 병사에게 저지당한 동안은 이동을 멈추고 근접 반격한다
            if (_blockedBy != null)
            {
                TickMelee();
                return;
            }

            Move();
        }

        void TickRegen()
        {
            if (Data.RegenPerSecond <= 0f)
                return;

            if (Hp >= MaxHp)
                return;

            Hp = Mathf.Min(MaxHp, Hp + Data.RegenPerSecond * Time.deltaTime);
        }

        void TickMelee()
        {
            _meleeTimer -= Time.deltaTime;

            if (_meleeTimer > 0f)
                return;

            _meleeTimer = Data.MeleeInterval;
            _blockedBy.TakeDamage(Data.MeleeDamage * AttackMultiplier(), Data.DisplayName);
        }

        void TickRangedAttack()
        {
            if (Data.RangedDamage <= 0f)
                return;

            _rangedTimer -= Time.deltaTime;

            if (_rangedTimer > 0f)
                return;

            var target = Soldier.FindNearest(transform.position, Data.RangedRange);

            if (target == null)
                return;

            _rangedTimer = Data.RangedInterval;
            target.TakeDamage(Data.RangedDamage * AttackMultiplier(), Data.DisplayName);

            if (GameLog.VerboseCombat)
                GameLog.Info("Dmg", $"{Data.DisplayName} 원거리 -> 병사: {Data.RangedDamage:F0}");
        }

        /// <summary>통제 상태 공격력 감소 보상(C11) 반영.</summary>
        float AttackMultiplier()
        {
            if (!IsControlled)
                return 1f;

            return 1f - RewardSystem.ControlledAttackReduction();
        }

        void Move()
        {
            if (_route == null || _route.PointCount < 2)
                return;

            float speed = _moveSpeed;

            if (IsSlowed)
                speed *= 1f - _slowPercent;

            _distance += speed * Time.deltaTime;

            if (_distance >= _route.TotalLength)
            {
                ReachExit();
                return;
            }

            Vector3 next = _route.GetPositionAtDistance(_distance) + Vector3.up * _yOffset;
            Vector3 delta = next - transform.position;
            delta.y = 0f;

            transform.position = next;

            if (delta.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }

        void ReachExit()
        {
            IsAlive = false;

            ReleaseFromBlocker();
            MonsterRegistry.Unregister(this);

            _onReachedExit?.Invoke(this);

            Destroy(gameObject);
        }

        /// <summary>최종 피해 적용. 계산은 DamageResolver를 거친다.</summary>
        public void ApplyDamage(float finalDamage, in DamageSource source)
        {
            if (!IsAlive)
                return;

            if (finalDamage <= 0f)
                return;

            Hp -= finalDamage;
            LastHitSource = source;
            ControlledAtLastHit = IsControlled;

            if (Hp > 0f)
                return;

            Die();
        }

        void Die()
        {
            IsAlive = false;

            ReleaseFromBlocker();
            MonsterRegistry.Unregister(this);

            _onDied?.Invoke(this);

            Destroy(gameObject);
        }

        /// <summary>이동속도 감소 (마도 타워 등). 더 강한 슬로우만 갱신한다.</summary>
        public void ApplySlow(float percent, float duration)
        {
            if (percent < _slowPercent && Time.time < _slowUntil)
                return;

            _slowPercent = Mathf.Clamp01(percent);
            _slowUntil = Time.time + duration;
        }

        /// <summary>경로를 따라 뒤로 밀어낸다. 저지 중이면 저지가 풀린다.</summary>
        public void Knockback(float pathDistance)
        {
            if (!IsAlive)
                return;

            if (Data.IsFlying)
                return;

            ReleaseFromBlocker();

            _distance = Mathf.Max(0f, _distance - pathDistance);
            transform.position = _route.GetPositionAtDistance(_distance) + Vector3.up * _yOffset;
        }

        /// <summary>기절. 면역 시간 동안은 다시 걸리지 않는다.</summary>
        public void ApplyStun(float duration, float immunitySeconds)
        {
            if (!IsAlive)
                return;

            if (Time.time < _stunImmuneUntil)
                return;

            _stunUntil = Time.time + duration;
            _stunImmuneUntil = _stunUntil + immunitySeconds;
        }

        /// <summary>마법 저항 단계를 영구히 한 단계 낮춘다 (최소 0).</summary>
        public void LowerMagicStage()
        {
            if (MagicStage <= 0)
                return;

            MagicStage--;

            if (GameLog.VerboseCombat)
                GameLog.Info("Dmg", $"{Data.DisplayName} 마법 저항 하락 -> {MagicStage}단계");
        }

        /// <summary>병사의 저지 시도. 지상 유닛만, 한 병사에게만 저지된다.</summary>
        public bool TryBlock(Soldier soldier)
        {
            if (!IsAlive)
                return false;

            if (Data.IsFlying)
                return false;

            if (_blockedBy != null)
                return false;

            if (!soldier.CanBlockMore())
                return false;

            _blockedBy = soldier;
            _meleeTimer = Data.MeleeInterval;
            soldier.NotifyBlocked(this);

            return true;
        }

        public void ReleaseBlock(Soldier soldier)
        {
            if (_blockedBy != soldier)
                return;

            _blockedBy = null;
        }

        void ReleaseFromBlocker()
        {
            if (_blockedBy == null)
                return;

            _blockedBy.NotifyTargetGone(this);
            _blockedBy = null;
        }
    }
}
