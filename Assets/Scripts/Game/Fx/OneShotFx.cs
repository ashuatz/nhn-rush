using UnityEngine;

namespace Rush.Fx
{
    /// <summary>
    /// 한 번 재생하고 스스로 사라지는 파티클 연출.
    ///
    /// 사망 파편은 죽은 대상의 색/알베도를 넘겨받아 그 캐릭터가 부서진 것처럼 보이게 하고,
    /// 연기 퍼프는 그대로 재생만 한다.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class OneShotFx : MonoBehaviour
    {
        /// <summary>파티클이 다 사라진 뒤 오브젝트를 지우기까지 두는 여유 시간.</summary>
        [SerializeField] float _destroyMargin = 0.3f;

        ParticleSystem _particles;
        ParticleSystemRenderer _renderer;
        MaterialPropertyBlock _block;

        void Awake()
        {
            _particles = GetComponent<ParticleSystem>();
            _renderer = GetComponent<ParticleSystemRenderer>();
            _block = new MaterialPropertyBlock();
        }

        /// <summary>프리팹을 그 자리에 한 번 재생한다. 프리팹이 비어 있으면 조용히 넘어간다.</summary>
        public static void Spawn(GameObject prefab, Vector3 position)
        {
            if (prefab == null)
                return;

            var instance = Instantiate(prefab, position, Quaternion.identity);
            var fx = instance.GetComponent<OneShotFx>();

            if (fx == null)
            {
                Destroy(instance, 2f);
                return;
            }

            fx.Play();
        }

        /// <summary>재생하고 수명이 끝나면 스스로 파괴한다.</summary>
        public void Play()
        {
            if (_particles == null)
                return;

            _particles.Clear(true);
            _particles.Play(true);

            Destroy(gameObject, GetTotalLifetime() + _destroyMargin);
        }

        /// <summary>
        /// 파편이 튀어나갈 방향. 파티클 Shape(콘)이 로컬 +Z를 향해 뿜으므로 회전만 맞춘다.
        /// </summary>
        public void SetDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.000001f)
                return;

            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>
        /// 원본 렌더러의 색과 알베도를 파편에 입힌다.
        /// 알베도가 없으면(더미 큐브 등) 색만 넘어가고 셰이더는 흰 텍스처를 쓴다.
        /// </summary>
        public void ApplySourceLook(Renderer source)
        {
            if (source == null || _renderer == null)
                return;

            var material = source.sharedMaterial;

            if (material == null)
                return;

            _renderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", ReadTint(material));

            var albedo = ReadAlbedo(material);

            if (albedo != null)
                _block.SetTexture("_BaseMap", albedo);

            _renderer.SetPropertyBlock(_block);
        }

        static Color ReadTint(Material material)
        {
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");

            if (material.HasProperty("_Color"))
                return material.GetColor("_Color");

            return Color.white;
        }

        static Texture ReadAlbedo(Material material)
        {
            if (material.HasProperty("_BaseMap"))
            {
                var map = material.GetTexture("_BaseMap");

                if (map != null)
                    return map;
            }

            if (material.HasProperty("_MainTex"))
                return material.GetTexture("_MainTex");

            return null;
        }

        float GetTotalLifetime()
        {
            var main = _particles.main;

            return main.duration + main.startLifetime.constantMax;
        }
    }
}
