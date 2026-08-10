using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 비행 몹의 연출 전용 모션. 제자리에서 위아래로 부유하고, 최근 피격 출처 쪽을 바라본다.
    /// 전투 로직은 건드리지 않고 로컬 트랜스폼만 흔든다 (최소 침투).
    ///
    /// 루트 트랜스폼은 Monster가 매 프레임 덮어쓰므로 모델 노드(Visual)에 붙이는 것이 전제다.
    /// </summary>
    public class FlyerHover : MonoBehaviour
    {
        /// <summary>부유 진폭. localPosition은 부모 공간이므로 루트 스케일이 1인 지금은 월드 단위와 같다.</summary>
        [SerializeField] float _bobAmplitude = 0.25f;

        /// <summary>부유 각속도(라디안/초). 클수록 빠르게 출렁인다.</summary>
        [SerializeField] float _bobSpeed = 3f;

        /// <summary>피격 후 공격자를 계속 바라보는 시간(초). 지나면 진행 방향으로 복귀한다.</summary>
        [SerializeField] float _lookDuration = 1.5f;

        /// <summary>바라보기/복귀 선회 속도(도/초).</summary>
        [SerializeField] float _lookTurnSpeed = 540f;

        Monster _monster;
        Vector3 _baseLocalPosition;

        /// <summary>개체마다 부유 위상을 흩는다. 한 배치가 한 몸처럼 같이 출렁이지 않게.</summary>
        float _phase;

        void Awake()
        {
            _monster = GetComponentInParent<Monster>();
            _baseLocalPosition = transform.localPosition;
            _phase = Random.Range(0f, Mathf.PI * 2f);
        }

        // Monster가 루트를 움직인 뒤에 얹는다. Update끼리는 순서가 보장되지 않아
        // 루트가 나중에 선회하면 그만큼 자식 회전이 밀려 떨림으로 보인다.
        void LateUpdate()
        {
            Bob();
            TurnToAttacker();
        }

        void Bob()
        {
            float offset = Mathf.Sin(Time.time * _bobSpeed + _phase) * _bobAmplitude;

            transform.localPosition = _baseLocalPosition + Vector3.up * offset;
        }

        /// <summary>공격자 쪽으로 선회한다. 바라볼 대상이 없으면 부모(진행 방향) 정렬로 되돌린다.</summary>
        void TurnToAttacker()
        {
            Quaternion target = ParentRotation();

            Vector3 toAttacker = AttackerDirection();

            if (toAttacker.sqrMagnitude > 0.000001f)
                target = Quaternion.LookRotation(toAttacker.normalized, Vector3.up);

            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, _lookTurnSpeed * Time.deltaTime);
        }

        Quaternion ParentRotation()
        {
            if (transform.parent == null)
                return transform.rotation;

            return transform.parent.rotation;
        }

        /// <summary>최근 피격 출처 방향(수평). 피격이 식었거나 출처가 없으면 zero.</summary>
        Vector3 AttackerDirection()
        {
            if (_monster == null)
                return Vector3.zero;

            if (Time.time - _monster.LastDamagedTime > _lookDuration)
                return Vector3.zero;

            var tower = _monster.LastHitSource.Tower;

            if (tower == null)
                return Vector3.zero;

            Vector3 to = tower.transform.position - transform.position;
            to.y = 0f;

            return to;
        }
    }
}
