using System.Collections.Generic;
using Rush.Data;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>발사체 1발의 모든 설정. 기본 공격과 추가 발사가 같은 경로를 쓰게 한다.</summary>
    public struct ProjectileConfig
    {
        public ProjectileMotion Motion;
        public GameObject ImpactPrefab;
        public DamageType DamageType;
        public float Speed;
        public float Damage;
        public float ArmorPierce;
        public float SplashRadius;
        public float SlowPercent;
        public float SlowDuration;
        public string SourceLabel;

        /// <summary>처치 시 추가 발사를 요청할 타워. 추가 발사로 생긴 발사체는 비워 둔다.</summary>
        public Tower BonusOwner;
    }

    /// <summary>
    /// 타워 발사체. MotionTrajectory가 그려 주는 곡선을 따라 날아가고 착탄 시 DamageResolver를 호출한다.
    /// 궤적은 "시작점 - 현재 표적 위치" 사이에서 매 프레임 다시 평가하므로 표적이 움직여도 따라간다.
    /// 표적이 먼저 죽으면 광역은 마지막 위치에 착탄하고, 단일 표적은 소멸한다.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        const float MinDuration = 0.05f;
        const float MaxLifetime = 6f;

        static readonly List<Monster> _splashBuffer = new List<Monster>(32);

        readonly MotionTrajectory _trajectory = new MotionTrajectory();

        ProjectileConfig _config;
        Monster _target;
        Vector3 _startPosition;
        Vector3 _lastTargetPos;
        Vector3 _previousPosition;

        float _duration;
        float _elapsed;
        bool _launched;

        public void Launch(in ProjectileConfig config, Monster target, Vector3 startPosition)
        {
            _config = config;
            _target = target;

            _startPosition = startPosition;
            _previousPosition = startPosition;

            _trajectory.Sample(_config.Motion);
            _lastTargetPos = target.transform.position + _trajectory.EndScatterOffset;

            // 비행 시간은 직선 거리 기준으로 잡는다 (곡선 길이로 잡으면 표적 이동에 따라 흔들린다)
            float distance = Vector3.Distance(startPosition, _lastTargetPos);
            _duration = Mathf.Max(MinDuration, distance / Mathf.Max(0.1f, _config.Speed));
            _elapsed = 0f;
            _launched = true;

            transform.position = startPosition;
        }

        void Update()
        {
            // Launch 전에 Update가 돌면(프리팹 오배치) 아무것도 하지 않는다
            if (!_launched)
                return;

            _elapsed += Time.deltaTime;

            if (_elapsed > MaxLifetime)
            {
                Despawn();
                return;
            }

            if (!RefreshTargetPosition())
                return;

            float t = Mathf.Clamp01(_elapsed / _duration);
            float eased = _config.Motion.EvaluateTime(t);

            Vector3 next = _trajectory.Evaluate(_startPosition, _lastTargetPos, eased);

            transform.position = next;
            FaceMovement(next);

            _previousPosition = next;

            if (t < 1f)
                return;

            Impact();
        }

        /// <summary>표적이 살아 있으면 도착점을 갱신한다. 더 진행할 수 없으면 false.</summary>
        bool RefreshTargetPosition()
        {
            if (_target != null && _target.IsAlive)
            {
                _lastTargetPos = _target.transform.position + _trajectory.EndScatterOffset;
                return true;
            }

            // 광역은 표적이 죽어도 마지막 지점에 떨어진다
            if (_config.SplashRadius > 0f)
                return true;

            Despawn();

            return false;
        }

        void FaceMovement(Vector3 next)
        {
            Vector3 delta = next - _previousPosition;

            if (delta.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);

            if (_config.Motion.SpinPerSecond > 0f)
                transform.Rotate(Vector3.forward, _config.Motion.SpinPerSecond * Time.deltaTime, Space.Self);
        }

        void Impact()
        {
            Monster killed;
            Vector3 killPosition;

            if (_config.SplashRadius > 0f)
            {
                killed = ImpactSplash(out killPosition);
            }
            else
            {
                killed = ImpactSingle(out killPosition);
            }

            SpawnBurst();

            if (killed != null)
                RequestBonusShots(killPosition, killed);

            Despawn();
        }

        /// <summary>이 착탄으로 죽은 몬스터를 돌려준다. 아무도 죽지 않았으면 null.</summary>
        Monster ImpactSingle(out Vector3 killPosition)
        {
            killPosition = transform.position;

            if (_target == null || !_target.IsAlive)
                return null;

            // Destroy는 프레임 끝에 반영되므로 위치는 피해 적용 전에 잡아 둔다
            Vector3 hitPosition = _target.transform.position;

            DamageResolver.Apply(_target, _config.Damage, _config.DamageType, _config.ArmorPierce, _config.SourceLabel);

            if (_config.SlowPercent > 0f && _target.IsAlive)
                _target.ApplySlow(_config.SlowPercent, _config.SlowDuration);

            if (_target.IsAlive)
                return null;

            killPosition = hitPosition;

            return _target;
        }

        Monster ImpactSplash(out Vector3 killPosition)
        {
            killPosition = _lastTargetPos;

            // 포병은 공중 공격 불가이므로 지상만 수집한다
            MonsterRegistry.CollectInRange(_lastTargetPos, _config.SplashRadius, includeFlying: false, _splashBuffer);

            Monster killed = null;

            foreach (var monster in _splashBuffer)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                Vector3 hitPosition = monster.transform.position;

                DamageResolver.Apply(monster, _config.Damage, _config.DamageType, _config.ArmorPierce, _config.SourceLabel);

                if (_config.SlowPercent > 0f && monster.IsAlive)
                    monster.ApplySlow(_config.SlowPercent, _config.SlowDuration);

                if (monster.IsAlive)
                    continue;

                // 여러 마리가 죽어도 추가 발사는 착탄당 한 번으로 묶는다 (발사체 폭주 방지)
                killed = monster;
                killPosition = hitPosition;
            }

            return killed;
        }

        /// <summary>
        /// 처치 시 추가 발사. 추가 발사로 생긴 발사체는 BonusOwner가 비어 있어 연쇄되지 않는다.
        /// 제외 대상은 "실제로 죽은 몬스터"다. 원래 표적을 넘기면 광역에서 살아남은 표적이 잘못 빠진다.
        /// </summary>
        void RequestBonusShots(Vector3 killPosition, Monster killed)
        {
            if (_config.BonusOwner == null)
                return;

            _config.BonusOwner.FireOnKillShots(killPosition, killed);
        }

        void SpawnBurst()
        {
            if (_config.ImpactPrefab == null)
                return;

            var go = Instantiate(_config.ImpactPrefab, transform.position, Quaternion.identity);
            var burst = go.GetComponent<ImpactBurst>();

            if (burst == null)
            {
                Destroy(go, 0.3f);
                return;
            }

            float radius = _config.SplashRadius;

            if (radius <= 0f)
                radius = 0.35f;

            burst.Play(radius);
        }

        /// <summary>발사체가 사라져도 잔상이 남도록 트레일을 떼어 낸 뒤 정리한다.</summary>
        void Despawn()
        {
            DetachTrail();

            Destroy(gameObject);
        }

        void DetachTrail()
        {
            var trail = GetComponentInChildren<TrailRenderer>();

            if (trail == null)
                return;

            trail.transform.SetParent(null);
            trail.emitting = false;

            Destroy(trail.gameObject, trail.time);
        }
    }
}
