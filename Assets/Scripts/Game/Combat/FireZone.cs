using System.Collections;
using System.Collections.Generic;
using Rush.Data;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 용의 숨결 화염 장판. 착탄 지점에 남아 지속 시간 동안 매초 범위 내 지상 적에게 피해를 준다.
    /// 화염 피해는 광역 태그가 아니다 (시트: 막증축 타워 스킬 정리 비고).
    /// 프리팹 없이 코드로 생성한다 (더미 비주얼: 착탄 이펙트를 틱마다 재생).
    /// </summary>
    public class FireZone : MonoBehaviour
    {
        static readonly List<Monster> _buffer = new List<Monster>(16);

        float _radius;
        float _dps;
        float _seconds;
        DamageSource _source;
        GameObject _burstPrefab;

        public static void Spawn(Vector3 position, float radius, float dps, float seconds,
            in DamageSource source, GameObject burstPrefab)
        {
            var go = new GameObject("FireZone");
            go.transform.position = position;

            var zone = go.AddComponent<FireZone>();
            zone._radius = radius;
            zone._dps = dps;
            zone._seconds = seconds;
            zone._source = source;
            zone._burstPrefab = burstPrefab;

            zone.StartCoroutine(zone.Run());
        }

        IEnumerator Run()
        {
            var wait = new WaitForSeconds(1f);
            int ticks = Mathf.Max(1, Mathf.RoundToInt(_seconds));

            for (int i = 0; i < ticks; i++)
            {
                Tick();

                yield return wait;
            }

            Destroy(gameObject);
        }

        void Tick()
        {
            // 비행 유닛은 장판에 닿지 않는다
            MonsterRegistry.CollectInRange(transform.position, _radius, includeFlying: false, _buffer);

            foreach (var monster in _buffer)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                DamageResolver.Apply(monster, _dps, DamageType.Physical, 0f, _source);
            }

            SpawnBurst();
        }

        void SpawnBurst()
        {
            if (_burstPrefab == null)
                return;

            var go = Instantiate(_burstPrefab, transform.position, Quaternion.identity);
            var burst = go.GetComponent<ImpactBurst>();

            if (burst == null)
            {
                Destroy(go, 0.3f);
                return;
            }

            burst.Play(_radius);
        }
    }
}
