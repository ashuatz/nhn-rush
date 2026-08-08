using UnityEngine;

namespace Rush.Stage
{
    /// <summary>
    /// 몬스터가 따라가는 경로 하나. 자식 Transform들이 웨이포인트다.
    /// 에디트 모드에서 기즈모로 경로를 확인한다. 기획서(코어 룰) 4장.
    ///
    /// 스테이지는 루트 4개(A1/A2/B1/B2)로 구성된다. 시작 지점 2곳에서 각각 두 갈래로 갈라져
    /// 종료 지점 2곳으로 들어가며, 네 루트는 맵 중앙에서 서로 교차한다.
    /// </summary>
    public class PathRoute : MonoBehaviour
    {
        /// <summary>루트 식별자 (A1/A2/B1/B2). 로그와 기즈모 색에 쓴다.</summary>
        [SerializeField] string _routeId = "A1";

        Transform[] _points;
        float[] _cumulative;

        public string RouteId => _routeId;

        /// <summary>에디터 셋업이 루트 ID를 채운다 (런타임에서 바꿀 일은 없다).</summary>
        public void SetRouteId(string id)
        {
            _routeId = id;
        }

        public int PointCount
        {
            get
            {
                EnsureCached();
                return _points.Length;
            }
        }

        /// <summary>경로 전체 길이. 몬스터 이동/넉백의 기준 좌표계.</summary>
        public float TotalLength
        {
            get
            {
                EnsureCached();

                if (_cumulative.Length == 0)
                    return 0f;

                return _cumulative[_cumulative.Length - 1];
            }
        }

        void Awake()
        {
            CachePoints();
        }

        /// <summary>자식 순서대로 웨이포인트를 다시 수집한다. 에디터 셋업에서도 호출.</summary>
        public void CachePoints()
        {
            int count = transform.childCount;
            _points = new Transform[count];

            for (int i = 0; i < count; i++)
                _points[i] = transform.GetChild(i);

            _cumulative = new float[count];

            for (int i = 1; i < count; i++)
            {
                float segment = Vector3.Distance(_points[i - 1].position, _points[i].position);
                _cumulative[i] = _cumulative[i - 1] + segment;
            }
        }

        public Vector3 GetPoint(int index)
        {
            EnsureCached();
            return _points[index].position;
        }

        /// <summary>시작점에서 distance만큼 진행한 경로 위 지점. 범위를 벗어나면 양끝으로 clamp.</summary>
        public Vector3 GetPositionAtDistance(float distance)
        {
            EnsureCached();

            if (_points.Length == 0)
                return transform.position;

            if (_points.Length == 1 || distance <= 0f)
                return _points[0].position;

            if (distance >= TotalLength)
                return _points[_points.Length - 1].position;

            for (int i = 1; i < _points.Length; i++)
            {
                if (distance > _cumulative[i])
                    continue;

                float segmentLength = _cumulative[i] - _cumulative[i - 1];

                if (segmentLength < 0.0001f)
                    return _points[i].position;

                float t = (distance - _cumulative[i - 1]) / segmentLength;

                return Vector3.Lerp(_points[i - 1].position, _points[i].position, t);
            }

            return _points[_points.Length - 1].position;
        }

        /// <summary>
        /// 이 경로 위에서 origin과 가장 가까운 지점. 거리 비교용 제곱거리를 함께 돌려준다.
        /// 병사 집결지 계산과 "가장 가까운 루트" 판정이 같은 계산을 쓴다.
        /// </summary>
        public Vector3 ClosestPoint(Vector3 origin, out float sqrDistance)
        {
            EnsureCached();

            sqrDistance = float.MaxValue;

            if (_points.Length == 0)
                return transform.position;

            if (_points.Length == 1)
            {
                sqrDistance = (_points[0].position - origin).sqrMagnitude;
                return _points[0].position;
            }

            Vector3 best = _points[0].position;

            for (int i = 0; i < _points.Length - 1; i++)
            {
                Vector3 candidate = ClosestPointOnSegment(_points[i].position, _points[i + 1].position, origin);
                float candidateSqr = (candidate - origin).sqrMagnitude;

                if (candidateSqr >= sqrDistance)
                    continue;

                best = candidate;
                sqrDistance = candidateSqr;
            }

            return best;
        }

        static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;

            if (lengthSqr < 0.0001f)
                return a;

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSqr);

            return a + ab * t;
        }

        void EnsureCached()
        {
            // 에디트 모드 호출(기즈모, 검증)에서는 Awake가 없으므로 지연 수집한다.
            if (_points == null || _points.Length != transform.childCount)
                CachePoints();
        }

        void OnDrawGizmos()
        {
            EnsureCached();

            if (_points.Length < 2)
                return;

            Gizmos.color = GizmoColor();

            for (int i = 0; i < _points.Length; i++)
            {
                if (_points[i] == null)
                    continue;

                Gizmos.DrawSphere(_points[i].position, 0.18f);

                if (i + 1 >= _points.Length || _points[i + 1] == null)
                    continue;

                Gizmos.DrawLine(_points[i].position, _points[i + 1].position);
            }
        }

        /// <summary>루트별 기즈모 색. 기획 스케치의 선 색을 그대로 쓴다.</summary>
        Color GizmoColor()
        {
            if (_routeId == "A1")
                return new Color(0.2f, 0.75f, 0.35f, 1f);

            if (_routeId == "A2")
                return new Color(0.55f, 0.35f, 0.15f, 1f);

            if (_routeId == "B1")
                return new Color(0.9f, 0.2f, 0.2f, 1f);

            if (_routeId == "B2")
                return new Color(0.2f, 0.45f, 0.95f, 1f);

            return new Color(0.7f, 0.7f, 0.7f, 1f);
        }
    }
}
