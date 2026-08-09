using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// Monster/Soldier의 공개 상태를 읽어 Animator 파라미터로 넘기는 얇은 어댑터.
    /// 전투 로직은 건드리지 않고 폴링만 한다 (임시 애니메이션 연결이라 최소 침투로 둔다).
    ///
    /// 모델 루트(Art 자식)에 붙는다. Monster/Soldier는 프리팹 루트에 있으므로 부모에서 찾는다.
    /// </summary>
    public class UnitAnimator : MonoBehaviour
    {
        /// <summary>이 속도(유닛/초) 이상이면 이동 중으로 본다.</summary>
        const float MoveThreshold = 0.05f;

        static readonly int MovingHash = Animator.StringToHash("Moving");
        static readonly int AttackingHash = Animator.StringToHash("Attacking");
        static readonly int DeadHash = Animator.StringToHash("Dead");

        Animator _animator;
        Monster _monster;
        Soldier _soldier;

        Vector3 _lastPosition;
        bool _dead;

        void Awake()
        {
            _animator = GetComponent<Animator>();
            _monster = GetComponentInParent<Monster>();
            _soldier = GetComponentInParent<Soldier>();

            _lastPosition = transform.position;
        }

        void Update()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;

            // 죽음은 한 번만 넘긴다. 시체가 남아 있는 동안 다시 트리거되지 않게.
            if (!_dead && IsDead())
            {
                _dead = true;
                _animator.SetBool(DeadHash, true);
            }

            if (_dead)
                return;

            _animator.SetBool(MovingHash, IsMoving());
            _animator.SetBool(AttackingHash, IsAttacking());
        }

        bool IsDead()
        {
            if (_monster != null)
                return !_monster.IsAlive;

            if (_soldier != null)
                return !_soldier.IsAlive;

            return false;
        }

        /// <summary>
        /// 실제 이동량으로 판정한다. 저지/기절/슬로우가 속도에 어떻게 반영되든
        /// 결과만 보면 되므로 상태 플래그를 하나하나 따라갈 필요가 없다.
        /// </summary>
        bool IsMoving()
        {
            Vector3 current = transform.position;
            float delta = (current - _lastPosition).magnitude;

            _lastPosition = current;

            // 일시정지(timeScale 0)에는 판정을 바꾸지 않고 직전 값을 유지한다
            if (Time.deltaTime <= 0f)
                return _animator.GetBool(MovingHash);

            return delta / Time.deltaTime > MoveThreshold;
        }

        bool IsAttacking()
        {
            // 몬스터는 병사에게 저지당한 동안 근접 공격을 반복한다
            if (_monster != null)
                return _monster.IsBlocked;

            if (_soldier != null)
                return _soldier.IsEngaged;

            return false;
        }
    }
}
