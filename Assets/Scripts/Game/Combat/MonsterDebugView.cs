using System.Collections.Generic;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// HP 디버그 뷰. 켜면 살아 있는 몬스터의 머티리얼을 HP 구간별 평면색으로 갈아끼운다.
    ///
    /// 개체마다 색을 넣는 대신 구간마다 머티리얼 에셋 하나를 공유한다.
    /// MaterialPropertyBlock으로 개별 색을 주면 그 렌더러의 SRP Batcher가 꺼지는데,
    /// 이 방식은 화면에 뜨는 머티리얼이 4종뿐이라 오히려 잘 묶인다.
    ///
    /// 병사(아군)는 건드리지 않는다. "평면색이면 적"이 그대로 진영 표식이 된다.
    /// </summary>
    public class MonsterDebugView : MonoBehaviour
    {
        /// <summary>HP 높은 구간부터 4개. 에디터 셋업(Rush/전체 셋업)이 채운다.</summary>
        [SerializeField] Material[] _bandMaterials;

        /// <summary>일반 몬스터 구간 경계. 이 값 이상이면 해당 구간.</summary>
        static readonly float[] NormalBands = { 0.75f, 0.50f, 0.25f };

        /// <summary>
        /// 보스 구간 경계. 보스는 최대 HP가 커서 같은 비율이라도 남은 실제 체력이 훨씬 많다
        /// (900짜리 보스의 25%는 225로, 민병 3마리 반이 넘는다).
        /// 일반 기준을 그대로 쓰면 빨강이 "아직 한참 남음"을 가리켜 신호가 무뎌지므로 아래로 당긴다.
        /// </summary>
        static readonly float[] BossBands = { 0.60f, 0.35f, 0.15f };

        /// <summary>죽은 몬스터 캐시를 걷어내는 주기 (프레임).</summary>
        const int PruneInterval = 120;

        sealed class Cached
        {
            public Renderer[] Renderers;

            /// <summary>렌더러별 원본 머티리얼 배열. 끌 때 그대로 되돌린다.</summary>
            public Material[][] Original;

            /// <summary>서브메시 수에 맞춰 미리 잡아둔 교체용 버퍼. 매번 할당하지 않으려고 들고 있는다.</summary>
            public Material[][] Swap;

            public int Band = -1;
        }

        readonly Dictionary<Monster, Cached> _cache = new Dictionary<Monster, Cached>(128);
        readonly List<Monster> _stale = new List<Monster>(32);

        bool _applied;
        int _frames;

        /// <summary>HUD 토글이 제어한다. 기본은 꺼둔다.</summary>
        public bool DisplayEnabled { get; set; }

        /// <summary>구간 머티리얼이 하나도 안 꽂혀 있으면 토글을 보여줄 이유가 없다.</summary>
        public bool IsReady => _bandMaterials != null && _bandMaterials.Length > 0;

        void OnDisable()
        {
            RestoreAll();
        }

        void LateUpdate()
        {
            if (!DisplayEnabled || !IsReady)
            {
                if (_applied)
                    RestoreAll();

                return;
            }

            _applied = true;

            foreach (var monster in MonsterRegistry.Active)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                Apply(monster);
            }

            // 죽은 개체의 캐시는 렌더러까지 같이 사라지므로 되돌릴 필요 없이 버리기만 한다
            if (++_frames < PruneInterval)
                return;

            _frames = 0;
            Prune();
        }

        void Apply(Monster monster)
        {
            if (!_cache.TryGetValue(monster, out var cached))
            {
                cached = Capture(monster);
                _cache[monster] = cached;
            }

            int band = BandOf(monster);

            if (band == cached.Band)
                return;

            cached.Band = band;

            var material = _bandMaterials[Mathf.Clamp(band, 0, _bandMaterials.Length - 1)];

            if (material == null)
                return;

            for (int i = 0; i < cached.Renderers.Length; i++)
            {
                var renderer = cached.Renderers[i];

                if (renderer == null)
                    continue;

                var slots = cached.Swap[i];

                for (int s = 0; s < slots.Length; s++)
                    slots[s] = material;

                renderer.sharedMaterials = slots;
            }
        }

        /// <summary>렌더러와 원본 머티리얼을 한 번만 훑어 캐시한다.</summary>
        static Cached Capture(Monster monster)
        {
            var found = monster.GetComponentsInChildren<Renderer>(true);
            var kept = new List<Renderer>(found.Length);

            foreach (var renderer in found)
            {
                // 파티클/트레일까지 평면색으로 덮으면 연출이 깨진다. 메시만 다룬다.
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    kept.Add(renderer);
            }

            var cached = new Cached
            {
                Renderers = kept.ToArray(),
                Original = new Material[kept.Count][],
                Swap = new Material[kept.Count][],
            };

            for (int i = 0; i < kept.Count; i++)
            {
                // 서브메시 수만큼 슬롯이 있으므로 배열을 통째로 다뤄야 한다
                var materials = kept[i].sharedMaterials;

                cached.Original[i] = materials;
                cached.Swap[i] = new Material[materials.Length];
            }

            return cached;
        }

        int BandOf(Monster monster)
        {
            float fraction = Mathf.Clamp01(monster.Hp / Mathf.Max(1f, monster.MaxHp));
            var bands = monster.Data != null && monster.Data.IsBoss ? BossBands : NormalBands;

            for (int i = 0; i < bands.Length; i++)
            {
                if (fraction >= bands[i])
                    return i;
            }

            return bands.Length;
        }

        void RestoreAll()
        {
            foreach (var pair in _cache)
            {
                var cached = pair.Value;

                for (int i = 0; i < cached.Renderers.Length; i++)
                {
                    if (cached.Renderers[i] == null)
                        continue;

                    cached.Renderers[i].sharedMaterials = cached.Original[i];
                }
            }

            _cache.Clear();
            _applied = false;
        }

        void Prune()
        {
            _stale.Clear();

            foreach (var pair in _cache)
            {
                if (pair.Key == null)
                    _stale.Add(pair.Key);
            }

            foreach (var monster in _stale)
                _cache.Remove(monster);

            _stale.Clear();
        }
    }
}
