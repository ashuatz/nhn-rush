using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 착탄 순간의 짧은 연출. 지정한 반경까지 부풀었다가 사라진다.
    /// 파티클 없이 큐브 하나로 처리하므로 리소스 교체 전까지의 자리표시자 역할도 한다.
    /// </summary>
    public class ImpactBurst : MonoBehaviour
    {
        [SerializeField] float _lifetime = 0.25f;
        [SerializeField] float _startScale = 0.2f;
        [SerializeField] float _spinPerSecond = 180f;

        float _targetScale = 1f;
        float _elapsed;

        public void Play(float radius)
        {
            _targetScale = Mathf.Max(0.2f, radius * 2f);
            _elapsed = 0f;

            transform.localScale = Vector3.one * _startScale;
        }

        void Update()
        {
            _elapsed += Time.deltaTime;

            float t = _elapsed / _lifetime;

            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // 커졌다가 다시 줄어드는 한 박자
            float pulse = Mathf.Sin(t * Mathf.PI);
            float scale = Mathf.Lerp(_startScale, _targetScale, pulse);

            transform.localScale = Vector3.one * scale;
            transform.Rotate(Vector3.up, _spinPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
