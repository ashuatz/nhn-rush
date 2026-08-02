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

        public int PointCount
        {
            get
            {
                EnsureCached();
                return _points.Length;
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
        }

        public Vector3 GetPoint(int index)
        {
            EnsureCached();
            return _points[index].position;
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
