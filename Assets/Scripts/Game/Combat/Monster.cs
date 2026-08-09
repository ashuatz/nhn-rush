using System;
using Rush.Data;
using Rush.Fx;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 몬스터 개체. 경로를 "진행 거리" 기준으로 이동하고(넉백/타겟 우선순위가 정확해진다),
    /// 체력/방어, 병사 저지 대응, 마법 재생, 원거리(궁병형) 공격을 처리한다.
    /// 방어등급은 스폰 시 런타임 단계(0=없음 ~ 5=면역)로 복사되어 보상이 영구적으로 낮출 수 있다.
    /// 죽음/출구 도달은 Initialize에서 받은 콜백으로 StageController에 보고한다.
    /// </summary>
    public class Monster : MonoBehaviour
    {
        const float FlyHeight = 2.2f;
        const float GroundHeight = 0f;

        /// <summary>데이터 오류(0 이하 이동속도)로 경로가 멈추는 것을 막는 최소 속도.</summary>
        const float MinMoveSpeed = 0.5f;

        /// <summary>경로 끝에 도달했다고 볼 반경. 몸체는 프로브보다 뒤에 있으므로 별도 판정이 필요하다.</summary>
        const float ArriveRadius = 0.25f;

        /// <summary>경로 중심선에서 좌우로 벌어지는 최대 거리. 같은 배치가 한 점에 겹쳐 나오는 것을 막는다.</summary>
        const float LaneSpread = 0.45f;

        /// <summary>스폰 시 진행 거리에 섞는 최대 지연 거리. 앞뒤로도 흩어 놓는다.</summary>
        const float SpawnDistanceJitter = 0.8f;

        /// <summary>스폰 시 진행 방향에 섞는 최대 요(yaw) 각도. 선회로 곧 정렬된다.</summary>
        const float SpawnYawJitter = 25f;

        /// <summary>레인 오프셋의 좌우 방향을 구할 때 경로를 앞뒤로 샘플링하는 거리.</summary>
        const float TangentSample = 0.25f;

        /// <summary>사망 파편 연출. 에디터 셋업에서 채운다.</summary>
        [SerializeField] GameObject _deathFx;

        /// <summary>프로브(경로 위 선행점)를 몸체보다 얼마나 앞에 둘지. 클수록 코너가 완만해진다.</summary>
        [SerializeField] float _probeLead = 1.6f;

        /// <summary>몸체의 최대 선회 속도(도/초). 낮을수록 크게 돈다.</summary>
        [SerializeField] float _turnSpeed = 300f;

        Action<Monster> _onDied;
        Action<Monster> _onReachedExit;
        PathRoute _route;
        float _distance;
        float _yOffset;
        float _moveSpeed;

        /// <summary>경로 중심선 기준 좌우 오프셋. 스폰 시 한 번 뽑아 끝까지 유지한다.</summary>
        float _laneOffset;

        float _slowPercent;
        float _slowUntil;
        float _stunUntil;
        float _stunImmuneUntil;

        Soldier _blockedBy;
        float _meleeTimer;
        float _rangedTimer;
        float _statMultiplier = 1f;

        public MonsterData Data { get; private set; }
        public float Hp { get; private set; }
        public float MaxHp { get; private set; }

        string _displayName;

        /// <summary>
        /// 로그와 HP 오버레이에 쓰는 이름. 중간 보스는 배정된 종류 이름("돌격대장" 등)으로 바뀐다.
        /// 데이터 이름은 웨이브 슬롯 이름("중간 보스 1")이라 판마다 달라지는 정체를 못 보여준다.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(_displayName))
                    return Data.DisplayName;

                return _displayName;
            }
        }

        /// <summary>웨이브 배수가 반영된 킬 보상. 스프레드시트(적 스탯 스케일링).</summary>
        public int GoldReward => ScaledGold(Data, _statMultiplier);

        /// <summary>
        /// 배수 반영 킬 보상(=적 단가)의 단일 계산 창구.
        /// 웨이브 예산 구성(WaveSpawner)과 실제 지급이 같은 값을 쓰게 해 예산 상한이 유지된다.
        /// </summary>
        public static int ScaledGold(MonsterData data, float statMultiplier)
        {
            return Mathf.RoundToInt(data.GoldReward * statMultiplier);
        }

        /// <summary>경로 진행 거리 (정확한 값). 넉백/귀환 등 같은 루트 안의 계산에 쓴다.</summary>
        public float PathProgress => _distance;

        /// <summary>
        /// 경로 진행률 0~1. 루트마다 길이가 달라 진행 거리를 그대로 비교하면
        /// 짧은 루트에서 출구에 더 붙은 적을 놓치므로, 루트 간 비교는 이 값으로 한다.
        /// </summary>
        public float PathProgressRatio
        {
            get
            {
                if (_route == null)
                    return 0f;

                float total = _route.TotalLength;

                if (total <= 0.0001f)
                    return 0f;

                return _distance / total;
            }
        }

        public bool IsAlive { get; private set; }

        /// <summary>현재 병사에게 저지당하고 있는지.</summary>
        public bool IsBlocked => _blockedBy != null;

        /// <summary>
        /// 마지막으로 피해를 입은 시각. HP 오버레이가 잠깐 바를 띄우는 데 쓴다.
        /// 리젠은 반영하지 않는다 - 상시 회복하는 적은 바가 계속 떠 있게 된다.
        /// </summary>
        public float LastDamagedTime { get; private set; } = float.NegativeInfinity;

        public bool IsSlowed => Time.time < _slowUntil;

        public bool IsStunned => Time.time < _stunUntil;

        /// <summary>감속 또는 저지 상태 (통제 계열 보상의 공통 조건).</summary>
        public bool IsControlled
        {
            get
            {
                if (IsBlocked)
                    return true;

                return IsSlowed;
            }
        }

        /// <summary>물리 방어 런타임 단계. 0=없음 ~ 4=면역 (단계당 25% 감쇄).</summary>
        public int PhysStage { get; private set; }

        /// <summary>마법 저항 런타임 단계. 보상으로 영구 하락 가능.</summary>
        public int MagicStage { get; private set; }

        /// <summary>마지막으로 피해를 준 출처. 처치 귀속(막타 골드 등)에 쓴다.</summary>
        public DamageSource LastHitSource { get; private set; }

        /// <summary>마지막 피격 시점에 통제(감속/저지) 상태였는지. 사망 시 저지가 풀려도 판정이 남는다.</summary>
        public bool ControlledAtLastHit { get; private set; }

        public void Initialize(MonsterData data, PathRoute route, float hpMultiplier, float statMultiplier,
            Action<Monster> onDied, Action<Monster> onReachedExit)
        {
            Data = data;
            _route = route;
            _onDied = onDied;
            _onReachedExit = onReachedExit;
            _statMultiplier = Mathf.Max(0.01f, statMultiplier);

            // 웨이브 배수는 체력/공격력/킬 보상에만 곱한다. 방어 단계는 그대로.
            MaxHp = data.MaxHp * hpMultiplier * _statMultiplier;
            Hp = MaxHp;
            IsAlive = true;

            PhysStage = (int)data.PhysicalDefense;
            MagicStage = (int)data.MagicalDefense;

            // 이동속도가 0 이하면 출구에도 못 가고 죽지도 않아 승패 판정이 막힌다
            _moveSpeed = data.MoveSpeed;

            if (_moveSpeed <= 0f)
            {
                GameLog.Warn("Wave", $"{data.DisplayName}: MoveSpeed가 {data.MoveSpeed} - 최소값 {MinMoveSpeed}로 보정");
                _moveSpeed = MinMoveSpeed;
            }

            if (data.IsFlying)
                _yOffset = FlyHeight;
            else
                _yOffset = GroundHeight;

            // 한 배치가 같은 점에 겹쳐 나오지 않도록 좌우 레인과 진행 거리를 흩는다.
            // 거리 지터는 그만큼 앞서 출발하는 것이므로 진행률/타겟 우선순위에도 반영된다 (최대 0.8, 경로 30 이상 기준 3% 미만).
            _laneOffset = UnityEngine.Random.Range(-LaneSpread, LaneSpread);
            _distance = Mathf.Min(UnityEngine.Random.Range(0f, SpawnDistanceJitter), _route.TotalLength);

            transform.position = PathPosition(_distance);

            // 스폰 직후 첫 프레임에 크게 도는 것을 막기 위해 프로브 쪽을 미리 바라본다
            FaceProbeImmediately();

            // 진행 방향에서 조금씩 어긋난 채 등장한다. 선회 한도 안에서 곧 경로에 정렬된다.
            transform.rotation *= Quaternion.Euler(0f, UnityEngine.Random.Range(-SpawnYawJitter, SpawnYawJitter), 0f);

            MonsterRegistry.Register(this);
        }

        /// <summary>
        /// 중간 보스 종류를 입힌다. 시트 기준으로 종류가 정하는 것은 체력 배율과 방어 배분뿐이며
        /// 공격력/킬 보상은 웨이브가 정한 값을 그대로 쓴다. 스폰 직후 한 번만 호출한다.
        /// </summary>
        public void ApplyBossArchetype(BossArchetypeDef def)
        {
            if (def == null)
                return;

            MaxHp *= Mathf.Max(0.01f, def.HpScale);
            Hp = MaxHp;

            PhysStage = (int)def.PhysicalDefense;
            MagicStage = (int)def.MagicalDefense;

            _displayName = def.DisplayName;

            GameLog.Info("Wave", $"중간 보스 등장 - {DisplayName} (체력 {MaxHp:F0})");
        }

        void OnDestroy()
        {
            MonsterRegistry.Unregister(this);
        }

        void Update()
        {
            if (!IsAlive)
                return;

            TickRegen();

            if (IsStunned)
                return;

            TickRangedAttack();

            // 병사에게 저지당한 동안은 이동을 멈추고 상대를 바라보며 근접 반격한다
            if (_blockedBy != null)
            {
                FaceBlocker();
                TickMelee();
                return;
            }

            Move();
        }

        void TickRegen()
        {
            if (Data.RegenPerSecond <= 0f)
                return;

            if (Hp >= MaxHp)
                return;

            Hp = Mathf.Min(MaxHp, Hp + Data.RegenPerSecond * Time.deltaTime);
        }

        void TickMelee()
        {
            _meleeTimer -= Time.deltaTime;

            if (_meleeTimer > 0f)
                return;

            _meleeTimer = Data.MeleeInterval;
            _blockedBy.TakeDamage(Data.MeleeDamage * AttackMultiplier(), DisplayName);
        }

        void TickRangedAttack()
        {
            if (Data.RangedDamage <= 0f)
                return;

            _rangedTimer -= Time.deltaTime;

            if (_rangedTimer > 0f)
                return;

            var target = Soldier.FindNearest(transform.position, Data.RangedRange);

            if (target == null)
                return;

            _rangedTimer = Data.RangedInterval;
            target.TakeDamage(Data.RangedDamage * AttackMultiplier(), DisplayName);

            if (GameLog.VerboseCombat)
                GameLog.Info("Dmg", $"{DisplayName} 원거리 -> 병사: {Data.RangedDamage:F0}");
        }

        /// <summary>웨이브 스탯 배수 x 통제 상태 공격력 감소 보상(C11).</summary>
        float AttackMultiplier()
        {
            if (!IsControlled)
                return _statMultiplier;

            return _statMultiplier * (1f - RewardSystem.ControlledAttackReduction());
        }

        /// <summary>
        /// 경로 진행 거리에 대응하는 실제 위치. 중심선에 레인 오프셋과 비행 높이를 더한 값이다.
        /// 위치를 순간이동시키는 모든 곳(스폰/넉백/귀환)이 이 창구를 쓴다.
        /// </summary>
        Vector3 PathPosition(float distance)
        {
            return _route.GetPositionAtDistance(distance) + LaneOffsetAt(distance) + Vector3.up * _yOffset;
        }

        /// <summary>해당 진행 거리에서의 좌우 오프셋. 경로 접선의 수직 방향으로 밀어낸다.</summary>
        Vector3 LaneOffsetAt(float distance)
        {
            if (Mathf.Approximately(_laneOffset, 0f))
                return Vector3.zero;

            // 경로 밖을 샘플링하면 앞뒤가 같은 끝점으로 clamp돼 접선이 0이 된다.
            // 그러면 종점 근처에서 오프셋이 갑자기 사라져 레인이 중심선으로 꺾인다.
            float center = Mathf.Clamp(distance, 0f, _route.TotalLength);

            Vector3 back = _route.GetPositionAtDistance(center - TangentSample);
            Vector3 ahead = _route.GetPositionAtDistance(center + TangentSample);

            Vector3 tangent = ahead - back;
            tangent.y = 0f;

            if (tangent.sqrMagnitude < 0.000001f)
                return Vector3.zero;

            return Vector3.Cross(Vector3.up, tangent.normalized) * _laneOffset;
        }

        void Move()
        {
            if (_route == null || _route.PointCount < 2)
                return;

            float speed = _moveSpeed;

            if (IsSlowed)
                speed *= 1f - _slowPercent;

            float step = speed * Time.deltaTime;

            _distance = Mathf.Min(_distance + step, _route.TotalLength);

            // 진행 거리를 다 쓴 뒤에는 선회 제한 없이 종점으로 직진한다.
            // 선회 반경 안쪽에 종점이 들어오면 추종만으로는 영원히 맴돌아 웨이브가 끝나지 않는다.
            if (_distance >= _route.TotalLength)
            {
                MoveToExit(step);
                return;
            }

            SteerTowards(ProbePosition(), step);
        }

        /// <summary>
        /// 프로브: 경로 위에서 진행 거리보다 선행 거리만큼 앞선 점 (레인 오프셋 포함).
        /// 몸체를 경로에 직접 찍지 않고 이 점을 향해 선회시키므로 웨이포인트 꺾임이 호로 펴진다.
        /// </summary>
        Vector3 ProbePosition()
        {
            return PathPosition(_distance + _probeLead);
        }

        /// <summary>경로 진행이 끝난 뒤 종점까지의 마무리 이동. 도달하면 출구 처리.</summary>
        void MoveToExit(float step)
        {
            Vector3 end = PathPosition(_route.TotalLength);

            Vector3 toEnd = end - transform.position;
            toEnd.y = 0f;

            if (toEnd.sqrMagnitude <= ArriveRadius * ArriveRadius)
            {
                ReachExit();
                return;
            }

            transform.position = Vector3.MoveTowards(transform.position, end, step);
            transform.rotation = Quaternion.LookRotation(toEnd.normalized, Vector3.up);
        }

        /// <summary>진행 방향을 프로브 쪽으로 선회 속도 한도 안에서 돌린 뒤 그 방향으로 한 스텝 전진한다.</summary>
        void SteerTowards(Vector3 probe, float step)
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.forward;

            forward.Normalize();

            Vector3 toProbe = probe - transform.position;
            toProbe.y = 0f;

            // 프로브가 사실상 겹치면 방향을 유지한 채 전진만 한다
            if (toProbe.sqrMagnitude > 0.000001f)
            {
                float maxRadians = _turnSpeed * Mathf.Deg2Rad * Time.deltaTime;

                forward = Vector3.RotateTowards(forward, toProbe.normalized, maxRadians, 0f);
            }

            // 높이는 경로를 그대로 따르고 선회는 수평면에서만 한다
            Vector3 next = transform.position + forward * step;
            next.y = _route.GetPositionAtDistance(_distance).y + _yOffset;

            transform.position = next;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        /// <summary>스폰/귀환처럼 위치를 순간이동시킨 직후 방향을 프로브 쪽으로 즉시 맞춘다.</summary>
        void FaceProbeImmediately()
        {
            Vector3 toProbe = ProbePosition() - transform.position;
            toProbe.y = 0f;

            if (toProbe.sqrMagnitude < 0.000001f)
                return;

            transform.rotation = Quaternion.LookRotation(toProbe.normalized, Vector3.up);
        }

        /// <summary>저지당한 동안 자기를 붙잡은 병사 쪽으로 선회한다. 이동 선회와 같은 한도를 쓴다.</summary>
        void FaceBlocker()
        {
            if (_blockedBy == null)
                return;

            Vector3 toBlocker = _blockedBy.transform.position - transform.position;
            toBlocker.y = 0f;

            if (toBlocker.sqrMagnitude < 0.000001f)
                return;

            Vector3 forward = transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.forward;

            float maxRadians = _turnSpeed * Mathf.Deg2Rad * Time.deltaTime;
            forward = Vector3.RotateTowards(forward.normalized, toBlocker.normalized, maxRadians, 0f);

            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        void ReachExit()
        {
            IsAlive = false;

            ReleaseFromBlocker();
            MonsterRegistry.Unregister(this);

            _onReachedExit?.Invoke(this);

            Destroy(gameObject);
        }

        /// <summary>최종 피해 적용. 계산은 DamageResolver를 거친다.</summary>
        public void ApplyDamage(float finalDamage, in DamageSource source)
        {
            if (!IsAlive)
                return;

            if (finalDamage <= 0f)
                return;

            Hp -= finalDamage;
            LastHitSource = source;
            ControlledAtLastHit = IsControlled;
            LastDamagedTime = Time.time;

            if (Hp > 0f)
                return;

            Die();
        }

        void Die()
        {
            IsAlive = false;

            ReleaseFromBlocker();
            MonsterRegistry.Unregister(this);

            PlayDeathFx();

            _onDied?.Invoke(this);

            Destroy(gameObject);
        }

        /// <summary>사망 파편. 자기 머티리얼 색/알베도를 조각에 입혀 그 적이 부서진 것처럼 보이게 한다.</summary>
        void PlayDeathFx()
        {
            if (_deathFx == null)
                return;

            var instance = Instantiate(_deathFx, transform.position + Vector3.up * 0.3f, Quaternion.identity);
            var fx = instance.GetComponent<OneShotFx>();

            if (fx == null)
            {
                Destroy(instance, 2f);
                return;
            }

            fx.SetDirection(GetDeathImpulseDirection());
            fx.ApplySourceLook(FindLookRenderer());
            fx.Play();
        }

        /// <summary>
        /// 파편에 입힐 색/알베도를 가져올 렌더러.
        /// 아트 모델이 배선된 프리팹은 더미 캡슐 렌더러가 꺼진 채 남아 있으므로 켜진 것만 고른다.
        /// </summary>
        Renderer FindLookRenderer()
        {
            var renderers = GetComponentsInChildren<Renderer>();

            foreach (var renderer in renderers)
            {
                if (renderer.enabled)
                    return renderer;
            }

            return null;
        }

        /// <summary>파편이 튈 방향. 마지막으로 때린 타워의 반대쪽으로 밀린 것처럼 만든다.</summary>
        Vector3 GetDeathImpulseDirection()
        {
            var tower = LastHitSource.Tower;

            if (tower == null)
                return Vector3.up;

            Vector3 away = transform.position - tower.transform.position;
            away.y = 0f;

            if (away.sqrMagnitude < 0.0001f)
                return Vector3.up;

            // 완전히 수평으로 튀면 바닥에 붙어 흐르므로 위쪽 성분을 섞는다
            return (away.normalized + Vector3.up * 1.2f).normalized;
        }

        /// <summary>이동속도 감소 (마도 타워 등). 더 강한 슬로우만 갱신한다.</summary>
        public void ApplySlow(float percent, float duration)
        {
            if (percent < _slowPercent && Time.time < _slowUntil)
                return;

            _slowPercent = Mathf.Clamp01(percent);
            _slowUntil = Time.time + duration;
        }

        /// <summary>경로를 따라 뒤로 밀어낸다. 저지 중이면 저지가 풀린다.</summary>
        public void Knockback(float pathDistance)
        {
            if (!IsAlive)
                return;

            if (Data.IsFlying)
                return;

            ReleaseFromBlocker();

            _distance = Mathf.Max(0f, _distance - pathDistance);
            transform.position = PathPosition(_distance);

            // 스냅한 위치에서 이전 방향을 그대로 두면 몇 프레임 동안 경로 밖으로 달린다
            FaceProbeImmediately();
        }

        /// <summary>기절. 면역 시간 동안은 다시 걸리지 않는다.</summary>
        public void ApplyStun(float duration, float immunitySeconds)
        {
            if (!IsAlive)
                return;

            if (Time.time < _stunImmuneUntil)
                return;

            _stunUntil = Time.time + duration;
            _stunImmuneUntil = _stunUntil + immunitySeconds;
        }

        /// <summary>웨이브 배수가 반영된 근접 공격력. 분기 스킬(정신차려!)이 읽는다.</summary>
        public float ScaledAttackDamage => Data.MeleeDamage * _statMultiplier;

        /// <summary>물리 방어 단계를 영구히 한 단계 낮춘다 (최소 0). 급조 철갑탄.</summary>
        public void LowerPhysStage()
        {
            if (PhysStage <= 0)
                return;

            PhysStage--;

            if (GameLog.VerboseCombat)
                GameLog.Info("Dmg", $"{DisplayName} 물리 방어 하락 -> {PhysStage}단계");
        }

        /// <summary>경로 시작 지점으로 되돌린다 (길잃은 방랑자). 저지 중이면 풀린다.</summary>
        public void TeleportToStart()
        {
            if (!IsAlive)
                return;

            ReleaseFromBlocker();

            _distance = 0f;
            transform.position = PathPosition(0f);

            FaceProbeImmediately();

            GameLog.Info("Skill", $"{DisplayName} 시작 지점으로 귀환");
        }

        /// <summary>마법 저항 단계를 영구히 한 단계 낮춘다 (최소 0).</summary>
        public void LowerMagicStage()
        {
            if (MagicStage <= 0)
                return;

            MagicStage--;

            if (GameLog.VerboseCombat)
                GameLog.Info("Dmg", $"{DisplayName} 마법 저항 하락 -> {MagicStage}단계");
        }

        /// <summary>병사의 저지 시도. 지상 유닛만, 한 병사에게만 저지된다.</summary>
        public bool TryBlock(Soldier soldier)
        {
            if (!IsAlive)
                return false;

            if (Data.IsFlying)
                return false;

            if (_blockedBy != null)
                return false;

            if (!soldier.CanBlockMore())
                return false;

            _blockedBy = soldier;
            _meleeTimer = Data.MeleeInterval;
            soldier.NotifyBlocked(this);

            return true;
        }

        public void ReleaseBlock(Soldier soldier)
        {
            if (_blockedBy != soldier)
                return;

            _blockedBy = null;
        }

        void ReleaseFromBlocker()
        {
            if (_blockedBy == null)
                return;

            _blockedBy.NotifyTargetGone(this);
            _blockedBy = null;
        }

        /// <summary>선행 거리/선회 속도를 눈으로 맞추기 위한 프로브 표시. 선택 중인 몬스터만 그린다.</summary>
        void OnDrawGizmosSelected()
        {
            if (_route == null)
                return;

            Vector3 probe = ProbePosition();

            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(probe, 0.12f);
            Gizmos.DrawLine(transform.position, probe);
        }
    }
}
