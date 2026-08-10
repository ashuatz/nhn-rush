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

        /// <summary>재생 배율 하한/상한. 0에 가까우면 발이 얼어붙고, 너무 크면 다리가 헛돈다.</summary>
        const float MinPlaybackRate = 0.4f;
        const float MaxPlaybackRate = 1.8f;

        /// <summary>배율 감쇠 시간(초). 저지/슬로우로 속도가 튈 때 재생 속도가 딸꾹질하지 않게.</summary>
        const float RateDamp = 0.15f;

        static readonly int MovingHash = Animator.StringToHash("Moving");
        static readonly int AttackingHash = Animator.StringToHash("Attacking");
        static readonly int DeadHash = Animator.StringToHash("Dead");
        static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");

        /// <summary>
        /// 이 속도(유닛/초)로 달릴 때 클립을 원래 속도로 재생한다. SwordRun이 자연스러워 보이는 기준값이며,
        /// 민병(1.6) 기준으로 잡았다. 보스(0.8)는 절반 속도, 정찰병(3.2)은 상한까지 빨라진다.
        /// </summary>
        [SerializeField] float _referenceMoveSpeed = 1.6f;

        Animator _animator;
        Monster _monster;
        Soldier _soldier;

        Vector3 _lastPosition;
        bool _dead;

        /// <summary>실측 이동 속도(유닛/초). 일시정지 프레임에는 갱신하지 않고 직전 값을 유지한다.</summary>
        float _speed;

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

            MeasureSpeed();

            _animator.SetBool(MovingHash, _speed > MoveThreshold);
            _animator.SetBool(AttackingHash, IsAttacking());

            ApplyRunRate();
        }

        /// <summary>
        /// 달리기 재생 속도를 실측 이동 속도에 맞춘다.
        /// 데이터의 MoveSpeed를 직접 읽지 않고 실측을 쓰는 이유는 IsMoving과 같다 -
        /// 슬로우/기절/버프가 어떻게 반영되든 결과 속도만 보면 되기 때문이다.
        /// </summary>
        void ApplyRunRate()
        {
            if (_referenceMoveSpeed <= 0.0001f)
                return;

            float rate = Mathf.Clamp(_speed / _referenceMoveSpeed, MinPlaybackRate, MaxPlaybackRate);

            _animator.SetFloat(MoveSpeedHash, rate, RateDamp, Time.deltaTime);
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
        /// 실제 이동량으로 속도를 잰다. 저지/기절/슬로우가 속도에 어떻게 반영되든
        /// 결과만 보면 되므로 상태 플래그를 하나하나 따라갈 필요가 없다.
        /// </summary>
        void MeasureSpeed()
        {
            Vector3 current = transform.position;
            float delta = (current - _lastPosition).magnitude;

            _lastPosition = current;

            // 일시정지(timeScale 0)에는 판정을 바꾸지 않고 직전 값을 유지한다
            if (Time.deltaTime <= 0f)
                return;

            _speed = delta / Time.deltaTime;
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
