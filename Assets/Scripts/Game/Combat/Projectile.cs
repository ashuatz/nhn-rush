using System.Collections;
using System.Collections.Generic;
using Rush.Data;
using Rush.Stage;
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
        public DamageSource Source;

        /// <summary>처치 시 추가 발사를 요청할 타워. 추가 발사로 생긴 발사체는 비워 둔다.</summary>
        public Tower BonusOwner;

        // ---------- 분기 스킬 부가 효과 (막증축 타워 스킬 정리) ----------

        /// <summary>헤드샷: 착탄 후 생존한 일반 몬스터 즉사 확률.</summary>
        public float InstantKillChance;

        /// <summary>급조 철갑탄: 물리 방어 1단계 영구 감소 확률.</summary>
        public float PhysShredChance;

        /// <summary>길잃은 방랑자: 시작 지점으로 되돌릴 확률 (보스 제외).</summary>
        public float TeleportChance;

        /// <summary>집속로켓: 착탄 시 기절 시간.</summary>
        public float StunDuration;

        /// <summary>용의 숨결: 착탄 지점 화염 장판 (초당 피해 / 지속 / 반경).</summary>
        public float GroundFireDps;
        public float GroundFireSeconds;
        public float GroundFireRadius;
    }

    /// <summary>
    /// 타워 발사체. MotionTrajectory가 그려 주는 곡선을 따라 날아가고 착탄 시 DamageResolver를 호출한다.
    /// 궤적은 "시작점 - 현재 표적 위치" 사이에서 매 프레임 다시 평가하므로 표적이 움직여도 따라간다.
    /// 표적이 먼저 죽으면 광역은 마지막 위치에 착탄하고, 단일 표적은 소멸한다.
    /// 착탄 시 보상 효과(중심 보너스/집속탄/연쇄 반응/넉백/기절)가 여기서 발동한다.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        const float MinDuration = 0.05f;
        const float MaxLifetime = 6f;

        static readonly List<Monster> _splashBuffer = new List<Monster>(32);
        static readonly List<Monster> _chainBuffer = new List<Monster>(16);
        static readonly List<Vector3> _killPositions = new List<Vector3>(16);

        readonly MotionTrajectory _trajectory = new MotionTrajectory();

        ProjectileConfig _config;
        Monster _target;
        Vector3 _startPosition;
        Vector3 _lastTargetPos;
        Vector3 _previousPosition;

        float _duration;
        float _elapsed;
        bool _launched;
        bool _impacted;

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
            if (!_launched || _impacted)
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
            _impacted = true;

            Monster killed;
            Vector3 killPosition;

            if (_config.SplashRadius > 0f)
            {
                killed = ImpactSplash(_config.Damage, out killPosition);
            }
            else
            {
                killed = ImpactSingle(out killPosition);
            }

            SpawnBurst(transform.position);

            // 용의 숨결: 착탄 지점에 화염 장판을 남긴다
            if (_config.GroundFireDps > 0f)
                FireZone.Spawn(_lastTargetPos, _config.GroundFireRadius, _config.GroundFireDps,
                    _config.GroundFireSeconds, _config.Source, _config.ImpactPrefab);

            if (killed != null)
                RequestBonusShots(killPosition, killed);

            // 집속탄(D09): 같은 지점에 지연 후 한 번 더 터진다
            if (_config.SplashRadius > 0f && RewardSystem.TryGetDoubleBlast(_config.Source, out float fraction, out float delay))
            {
                StartCoroutine(SecondBlast(fraction, delay));
                return;
            }

            Despawn();
        }

        IEnumerator SecondBlast(float fraction, float delay)
        {
            HideVisuals();

            yield return new WaitForSeconds(delay);

            var killed = ImpactSplash(_config.Damage * fraction, out Vector3 killPosition);

            SpawnBurst(_lastTargetPos);

            if (killed != null)
                RequestBonusShots(killPosition, killed);

            Despawn();
        }

        void HideVisuals()
        {
            DetachTrail();

            foreach (var renderer in GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
        }

        /// <summary>이 착탄으로 죽은 몬스터를 돌려준다. 아무도 죽지 않았으면 null.</summary>
        Monster ImpactSingle(out Vector3 killPosition)
        {
            killPosition = transform.position;

            if (_target == null || !_target.IsAlive)
                return null;

            // Destroy는 프레임 끝에 반영되므로 위치는 피해 적용 전에 잡아 둔다
            Vector3 hitPosition = _target.transform.position;

            DamageResolver.Apply(_target, _config.Damage, _config.DamageType, _config.ArmorPierce, _config.Source);

            // 헤드샷: 생존한 일반 몬스터 즉사 확률
            if (_target != null && _target.IsAlive && _config.InstantKillChance > 0f && !_target.Data.IsBoss)
            {
                if (Random.value < _config.InstantKillChance)
                {
                    GameLog.Info("Skill", $"{_config.Source.Label} 즉사 발동 -> {_target.Data.DisplayName}");
                    DamageResolver.Apply(_target, _target.Hp + 1f, DamageType.True, 0f, _config.Source);
                }
            }

            if (_target != null && _target.IsAlive)
            {
                if (_config.SlowPercent > 0f)
                    _target.ApplySlow(_config.SlowPercent, _config.SlowDuration);

                ApplyBranchRiders(_target);
                RewardSystem.ApplyOnHitRiders(_config.Source, _target);

                return null;
            }

            NotifyKillToSourceTower();

            killPosition = hitPosition;

            return _target;
        }

        /// <summary>분기 스킬 착탄 효과 (생존한 표적 대상): 방깎 / 귀환 / 기절.</summary>
        void ApplyBranchRiders(Monster target)
        {
            if (Luck.Roll(_config.PhysShredChance))
                target.LowerPhysStage();

            if (!target.Data.IsBoss && Luck.Roll(_config.TeleportChance))
                target.TeleportToStart();

            if (_config.StunDuration > 0f)
                target.ApplyStun(_config.StunDuration, 1f);
        }

        /// <summary>처치를 출처 타워에 알린다 (피의 향연 트리거).</summary>
        void NotifyKillToSourceTower()
        {
            if (_config.Source.Tower != null)
                _config.Source.Tower.NotifyKillByThisTower();
        }

        Monster ImpactSplash(float damage, out Vector3 killPosition)
        {
            killPosition = _lastTargetPos;

            // 포병은 공중 공격 불가이므로 지상만 수집한다
            MonsterRegistry.CollectInRange(_lastTargetPos, _config.SplashRadius, includeFlying: false, _splashBuffer);

            // 폭발 중심 보너스(D08): 중심에 가장 가까운 적 1기
            Monster centerMost = null;
            float centerBonus = RewardSystem.SplashCenterBonus(_config.Source);

            if (centerBonus > 0f)
            {
                float bestSqr = float.MaxValue;

                foreach (var monster in _splashBuffer)
                {
                    if (monster == null || !monster.IsAlive)
                        continue;

                    float distSqr = (monster.transform.position - _lastTargetPos).sqrMagnitude;

                    if (distSqr >= bestSqr)
                        continue;

                    centerMost = monster;
                    bestSqr = distSqr;
                }
            }

            Monster killed = null;
            _killPositions.Clear();

            foreach (var monster in _splashBuffer)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                Vector3 hitPosition = monster.transform.position;

                float perTarget = damage;

                if (monster == centerMost)
                    perTarget *= 1f + centerBonus;

                DamageResolver.Apply(monster, perTarget, _config.DamageType, _config.ArmorPierce, _config.Source);

                if (monster != null && monster.IsAlive)
                {
                    if (_config.SlowPercent > 0f)
                        monster.ApplySlow(_config.SlowPercent, _config.SlowDuration);

                    ApplyBranchRiders(monster);
                    RewardSystem.ApplyOnHitRiders(_config.Source, monster);
                    continue;
                }

                // 여러 마리가 죽어도 추가 발사(처치 트리거)는 착탄당 한 번으로 묶는다 (발사체 폭주 방지)
                killed = monster;
                killPosition = hitPosition;
                _killPositions.Add(hitPosition);
            }

            if (killed != null)
                NotifyKillToSourceTower();

            // 연쇄 반응(G02): 광역 처치마다 그 자리에서 한 번 더 터진다 (연쇄의 연쇄는 없음)
            if (_killPositions.Count > 0 && RewardSystem.TryGetChainExplosion(_config.Source, out float chainFraction, out float chainRadius))
                ExplodeChain(damage * chainFraction, chainRadius);

            return killed;
        }

        void ExplodeChain(float chainDamage, float radius)
        {
            var chainSource = _config.Source.AsChain();

            foreach (var position in _killPositions)
            {
                MonsterRegistry.CollectInRange(position, radius, includeFlying: false, _chainBuffer);

                foreach (var monster in _chainBuffer)
                {
                    if (monster == null || !monster.IsAlive)
                        continue;

                    DamageResolver.Apply(monster, chainDamage, _config.DamageType, _config.ArmorPierce, chainSource);
                }

                SpawnBurst(position);
            }
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

        void SpawnBurst(Vector3 position)
        {
            if (_config.ImpactPrefab == null)
                return;

            var go = Instantiate(_config.ImpactPrefab, position, Quaternion.identity);
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
