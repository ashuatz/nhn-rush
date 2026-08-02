using System;
using Rush.Data;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 몬스터 개체. 경로 이동, 체력/방어, 병사 저지 대응, 마법 재생, 원거리(궁병형) 공격.
    /// 죽음/출구 도달은 Initialize에서 받은 콜백으로 StageController에 보고한다.
    /// </summary>
    public class Monster : MonoBehaviour
    {
        const float FlyHeight = 2.2f;
        const float GroundHeight = 0f;
        const float ArriveThreshold = 0.05f;

        /// <summary>데이터 오류(0 이하 이동속도)로 경로가 멈추는 것을 막는 최소 속도.</summary>
        const float MinMoveSpeed = 0.5f;

        Action<Monster> _onDied;
        Action<Monster> _onReachedExit;
        PathRoute _route;
        int _pointIndex;
        float _yOffset;
        float _moveSpeed;

        float _slowPercent;
        float _slowUntil;

        Soldier _blockedBy;
        float _meleeTimer;
        float _rangedTimer;

        public MonsterData Data { get; private set; }
        public float Hp { get; private set; }
        public float MaxHp { get; private set; }

        /// <summary>경로를 따라 진행한 누적 거리. 타워 타겟팅 우선순위에 쓴다.</summary>
        public float PathProgress { get; private set; }

        public bool IsAlive { get; private set; }

        /// <summary>현재 병사에게 저지당하고 있는지. 병사 표적 선정에 쓴다.</summary>
        public bool IsBlocked => _blockedBy != null;

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

            _pointIndex = 1;
            transform.position = WithOffset(route.GetPoint(0));

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
            _blockedBy.TakeDamage(Data.MeleeDamage, Data.DisplayName);
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
            target.TakeDamage(Data.RangedDamage, Data.DisplayName);

            if (GameLog.VerboseCombat)
                GameLog.Info("Dmg", $"{Data.DisplayName} 원거리 -> 병사: {Data.RangedDamage:F0}");
        }

        void Move()
        {
            if (_route == null || _pointIndex >= _route.PointCount)
                return;

            float speed = _moveSpeed;

            if (Time.time < _slowUntil)
                speed *= 1f - _slowPercent;

            Vector3 target = WithOffset(_route.GetPoint(_pointIndex));
            Vector3 toTarget = target - transform.position;
            float step = speed * Time.deltaTime;

            PathProgress += step;

            if (toTarget.magnitude <= step + ArriveThreshold)
            {
                transform.position = target;
                _pointIndex++;

                if (_pointIndex >= _route.PointCount)
                    ReachExit();

                return;
            }

            transform.position += toTarget.normalized * step;
        }

        Vector3 WithOffset(Vector3 groundPoint)
        {
            return groundPoint + Vector3.up * _yOffset;
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
        public void ApplyDamage(float finalDamage, string source)
        {
            if (!IsAlive)
                return;

            if (finalDamage <= 0f)
                return;

            Hp -= finalDamage;

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

        /// <summary>이동속도 감소 (마도 타워). 더 강한 슬로우만 갱신한다.</summary>
        public void ApplySlow(float percent, float duration)
        {
            if (percent < _slowPercent && Time.time < _slowUntil)
                return;

            _slowPercent = Mathf.Clamp01(percent);
            _slowUntil = Time.time + duration;
        }

        /// <summary>병사의 저지 시도. 지상 유닛 1:1 저지만 허용.</summary>
        public bool TryBlock(Soldier soldier)
        {
            if (!IsAlive)
                return false;

            if (Data.IsFlying)
                return false;

            if (_blockedBy != null)
                return false;

            _blockedBy = soldier;
            _meleeTimer = Data.MeleeInterval;

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
