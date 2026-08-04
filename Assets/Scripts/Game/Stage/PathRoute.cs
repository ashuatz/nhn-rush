using UnityEngine;

namespace Rush.Stage
{
    /// <summary>
    /// 몬스터가 따라가는 단일 경로. 자식 Transform들이 웨이포인트다.
    /// 에디트 모드에서 기즈모로 경로를 확인한다. 기획서(코어 룰) 4장.
    /// </summary>
    public class PathRoute : MonoBehaviour
    {
        Transform[] _points;
        float[] _cumulative;

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

            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 1f);

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
    }
}
