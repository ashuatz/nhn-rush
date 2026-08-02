using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Rush.Data
{
    /// <summary>발사체 궤적 종류. 계열마다 다른 연출을 주기 위한 프리셋 구분값.</summary>
    public enum MotionKind
    {
        Straight = 0,
        Arc = 1,
        Lob = 2,
        Wander = 3,
    }

    /// <summary>
    /// 발사체 연출 설정. 두 가지 기법을 하나로 합친다.
    /// (1) 시작-끝 사이에 랜덤 제어점을 잡는 2차 베지어 - 크게 휘는 궤적
    /// (2) 진행축에 수직인 평면에서의 연속 랜덤 워크 - 잔잔하게 흔들리는 궤적
    /// 둘 다 0으로 두면 직선이 된다.
    /// </summary>
    [Serializable]
    public class ProjectileMotion
    {
        public MotionKind Kind = MotionKind.Straight;

        [Header("발사")]
        [Tooltip("한 번 공격에 쏘는 발사체 수. 피해는 균등 분배된다. 0이면 미설정으로 보고 셋업이 프리셋을 채운다.")]
        public int ShotCount;
        public float ShotInterval = 0.08f;
        [Tooltip("착탄 지점을 흩뜨리는 반경. 단일 표적 피해에는 영향 없고 연출과 광역 중심만 흔든다.")]
        public float EndScatter;
        public float SpinPerSecond;

        [Header("휘어짐 (베지어 제어점)")]
        [Range(0.05f, 0.95f)] public float SampleMin = 0.2f;
        [Range(0.05f, 0.95f)] public float SampleMax = 0.4f;
        [Tooltip("제어점 거리 = Factor / 비행거리 를 Min~Max로 clamp 한 값")]
        public float BulgeFactor;
        public float BulgeMin;
        public float BulgeMax;
        [Tooltip("켜면 항상 월드 위쪽으로 부푼다 (포병 곡사)")]
        public bool BulgeWorldUp;
        [Tooltip("진행축 기준 롤 각도 = Random(-N..N) * 15도. 0이면 항상 위쪽, 12면 전방향")]
        public int RollSteps = 2;

        [Header("흔들림 (연속 랜덤)")]
        public float WanderAmplitude;
        [Range(0f, 1f)] public float WanderTurn = 0.6f;

        [Header("시간")]
        public AnimationCurve TimeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public bool IsConfigured => ShotCount > 0;

        public float EvaluateTime(float t)
        {
            // 키가 모자란 커브(직렬화 초기값 등)는 선형으로 취급한다
            if (TimeCurve == null || TimeCurve.length < 2)
                return t;

            return TimeCurve.Evaluate(t);
        }
    }

    /// <summary>
    /// 발사 순간에 랜덤을 한 번 뽑아 두고, 이후에는 위치만 계산한다.
    /// 랜덤 성분을 "진행축 기준 로컬 좌표"로 들고 있어서 표적이 움직여도 궤적 모양이 유지된다.
    /// </summary>
    public class MotionTrajectory
    {
        const int WanderSamples = 9;

        static readonly Vector2[] _emptyWander = new Vector2[0];

        float _sampleT;
        Vector2 _bulgeLocal;
        bool _bulgeWorldUp;
        float _bulgeFactor;
        float _bulgeMin;
        float _bulgeMax;

        Vector2[] _wander = _emptyWander;
        float _wanderAmplitude;

        // 진행축 직교 프레임의 기준축. 첫 평가 때 한 번 정하고 이후에는 재직교화만 한다.
        // 매 프레임 월드 up에서 새로 만들면 진행축이 수직에 가까워질 때 프레임이 급전환되어 궤적이 튄다.
        Vector3 _frameRight;
        bool _frameReady;

        public Vector3 EndScatterOffset { get; private set; }

        public void Sample(ProjectileMotion motion)
        {
            _sampleT = Random.Range(motion.SampleMin, motion.SampleMax);
            _bulgeWorldUp = motion.BulgeWorldUp;
            _bulgeFactor = motion.BulgeFactor;
            _bulgeMin = motion.BulgeMin;
            _bulgeMax = motion.BulgeMax;

            // 진행축을 중심으로 회전시킨 방향으로 부풀린다. RollSteps가 0이면 항상 위쪽.
            int steps = Mathf.Max(0, motion.RollSteps);
            float roll = Random.Range(-steps, steps + 1) * 15f * Mathf.Deg2Rad;
            _bulgeLocal = new Vector2(Mathf.Sin(roll), Mathf.Cos(roll));

            _wanderAmplitude = motion.WanderAmplitude;

            if (_wanderAmplitude > 0f)
            {
                BuildWander(motion.WanderTurn);
            }
            else
            {
                _wander = _emptyWander;
            }

            EndScatterOffset = Random.insideUnitSphere * motion.EndScatter;
            EndScatterOffset = new Vector3(EndScatterOffset.x, Mathf.Abs(EndScatterOffset.y) * 0.3f, EndScatterOffset.z);

            _frameReady = false;
        }

        /// <summary>
        /// 참조 기법: 이전 방향을 랜덤 회전시켜 다음 방향을 만들고,
        /// 누산값의 역방향과 보간해 원점에서 너무 멀어지지 않게 잡아 준다.
        /// </summary>
        void BuildWander(float turn)
        {
            if (_wander.Length != WanderSamples)
                _wander = new Vector2[WanderSamples];

            Vector2 dir = Random.insideUnitCircle.normalized;
            Vector2 offset = Vector2.zero;
            float maxMagnitude = 0.0001f;

            _wander[0] = Vector2.zero;

            for (int i = 1; i < WanderSamples; i++)
            {
                float angle = Random.Range(-90f, 90f) * turn * Mathf.Deg2Rad;
                dir = Rotate(dir, angle).normalized * Random.Range(0.4f, 1f);
                dir = Vector2.Lerp(-offset, dir, 0.9f);

                offset += dir;
                _wander[i] = offset;

                if (offset.magnitude > maxMagnitude)
                    maxMagnitude = offset.magnitude;
            }

            // 진폭을 1로 정규화하고 양끝이 0이 되도록 사인 창을 씌운다 (시작/도착점 정확히 맞추기)
            for (int i = 0; i < WanderSamples; i++)
            {
                float window = Mathf.Sin(Mathf.PI * i / (WanderSamples - 1f));
                _wander[i] = _wander[i] / maxMagnitude * window;
            }
        }

        static Vector2 Rotate(Vector2 v, float radians)
        {
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);

            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        /// <summary>t(0~1) 지점의 월드 위치. end는 매 프레임 바뀌어도 된다.</summary>
        public Vector3 Evaluate(Vector3 start, Vector3 end, float t)
        {
            Vector3 axis = end - start;
            float distance = axis.magnitude;

            if (distance < 0.0001f)
                return end;

            Vector3 forward = axis / distance;
            Vector3 right = ResolveRight(forward);
            Vector3 up = Vector3.Cross(forward, right);

            Vector3 position = EvaluateBezier(start, end, distance, right, up, t);

            if (_wanderAmplitude > 0f && _wander.Length == WanderSamples)
            {
                Vector2 lateral = SampleWander(t);
                position += (right * lateral.x + up * lateral.y) * _wanderAmplitude;
            }

            return position;
        }

        /// <summary>
        /// 진행축에 수직인 기준축을 돌려준다.
        /// 첫 호출에서 정한 축을 계속 재직교화해서 쓰므로, 표적이 움직여 진행축이 돌아도 궤적이 튀지 않는다.
        /// </summary>
        Vector3 ResolveRight(Vector3 forward)
        {
            if (_frameReady)
            {
                Vector3 carried = _frameRight - forward * Vector3.Dot(_frameRight, forward);

                if (carried.sqrMagnitude >= 0.0001f)
                    return carried.normalized;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);

            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.Cross(Vector3.forward, forward);
            }

            right.Normalize();

            _frameRight = right;
            _frameReady = true;

            return right;
        }

        Vector3 EvaluateBezier(Vector3 start, Vector3 end, float distance, Vector3 right, Vector3 up, float t)
        {
            float bulge = 0f;

            if (_bulgeFactor > 0f)
                bulge = Mathf.Clamp(_bulgeFactor / distance, _bulgeMin, _bulgeMax);

            if (bulge <= 0f)
                return Vector3.Lerp(start, end, t);

            Vector3 bulgeDir;

            if (_bulgeWorldUp)
            {
                bulgeDir = Vector3.up;
            }
            else
            {
                bulgeDir = right * _bulgeLocal.x + up * _bulgeLocal.y;
            }

            Vector3 control = Vector3.Lerp(start, end, _sampleT) + bulgeDir * bulge;

            Vector3 a = Vector3.Lerp(start, control, t);
            Vector3 b = Vector3.Lerp(control, end, t);

            return Vector3.Lerp(a, b, t);
        }

        Vector2 SampleWander(float t)
        {
            float scaled = Mathf.Clamp01(t) * (WanderSamples - 1);
            int index = Mathf.FloorToInt(scaled);

            if (index >= WanderSamples - 1)
                return _wander[WanderSamples - 1];

            float frac = scaled - index;

            return Vector2.Lerp(_wander[index], _wander[index + 1], frac);
        }
    }
}
