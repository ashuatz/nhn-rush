using System.Collections.Generic;
using Rush.Data;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 타워 발사체 (궁병/마도/포병 공용). 표적을 추적하고 착탄 시 DamageResolver를 호출한다.
    /// 표적이 먼저 죽으면: 광역이면 마지막 위치에 착탄, 단일이면 소멸.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        const float HitThreshold = 0.25f;
        const float MaxLifetime = 6f;

        static readonly List<Monster> _splashBuffer = new List<Monster>(32);

        Monster _target;
        Vector3 _lastTargetPos;
        float _speed;
        float _damage;
        DamageType _damageType;
        float _armorPierce;
        float _splashRadius;
        float _slowPercent;
        float _slowDuration;
        string _sourceLabel;
        float _lifetime;

        public void Launch(Monster target, float speed, float damage, DamageType damageType,
            float armorPierce, float splashRadius, float slowPercent, float slowDuration, string sourceLabel)
        {
            _target = target;
            _lastTargetPos = target.transform.position;
            _speed = speed;
            _damage = damage;
            _damageType = damageType;
            _armorPierce = armorPierce;
            _splashRadius = splashRadius;
            _slowPercent = slowPercent;
            _slowDuration = slowDuration;
            _sourceLabel = sourceLabel;
        }

        void Update()
        {
            _lifetime += Time.deltaTime;

            if (_lifetime > MaxLifetime)
            {
                Destroy(gameObject);
                return;
            }

            // 표적 생존 시 위치 갱신, 사망 시 단일 표적 발사체는 소멸
            if (_target != null && _target.IsAlive)
            {
                _lastTargetPos = _target.transform.position;
            }
            else if (_splashRadius <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 toTarget = _lastTargetPos - transform.position;
            float step = _speed * Time.deltaTime;

            if (toTarget.magnitude <= step + HitThreshold)
            {
                Impact();
                return;
            }

            transform.position += toTarget.normalized * step;
        }

        void Impact()
        {
            if (_splashRadius > 0f)
            {
                ImpactSplash();
            }
            else
            {
                ImpactSingle();
            }

            Destroy(gameObject);
        }

        void ImpactSingle()
        {
            if (_target == null || !_target.IsAlive)
                return;

            DamageResolver.Apply(_target, _damage, _damageType, _armorPierce, _sourceLabel);

            if (_slowPercent > 0f && _target != null && _target.IsAlive)
                _target.ApplySlow(_slowPercent, _slowDuration);
        }

        void ImpactSplash()
        {
            // 포병은 공중 공격 불가이므로 지상만 수집한다
            MonsterRegistry.CollectInRange(_lastTargetPos, _splashRadius, includeFlying: false, _splashBuffer);

            foreach (var monster in _splashBuffer)
            {
                DamageResolver.Apply(monster, _damage, _damageType, _armorPierce, _sourceLabel);

                if (_slowPercent > 0f && monster != null && monster.IsAlive)
                    monster.ApplySlow(_slowPercent, _slowDuration);
            }
        }
    }
}
