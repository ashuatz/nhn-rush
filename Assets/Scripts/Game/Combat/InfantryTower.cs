using System;
using Rush.Data;
using Rush.Stage;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 보병 계열: 경로 위 집결지에 병사를 소환/유지해 지상 몬스터를 저지한다.
    /// 직접 공격은 하지 않는다. 기획서(코어 룰) 2장 보병 계열.
    /// </summary>
    public class InfantryTower : Tower
    {
        const float SpawnSpreadRadius = 0.45f;

        /// <summary>
        /// 병사 자리. 인덱스가 곧 집결지 주변 배치 순번이라, 죽었다 돌아와도 원래 서 있던 자리로 온다.
        /// 자리를 고정하지 않으면 부분 충원 때 살아 있는 병사와 같은 지점에 겹쳐 선다.
        /// </summary>
        Soldier[] _slots = new Soldier[0];

        /// <summary>
        /// 자리별 충원 대기 시간. 병사마다 따로 흐르므로 뒤이어 죽은 병사가 앞선 병사의 충원을 밀어내지 않는다.
        /// 0 이하면 대기 없이 채운다.
        /// </summary>
        float[] _slotTimers = new float[0];

        Vector3 _rallyPoint;
        bool _rallyReady;

        /// <summary>병사들이 지키는 지점. 플레이어가 옮기지 않으면 타워에서 가장 가까운 경로 지점이다.</summary>
        public Vector3 RallyPoint => _rallyPoint;

        /// <summary>
        /// 타워에서 랠리 포인트를 놓을 수 있는 최대 거리 (시트: 병영 사거리).
        /// 보상 E06(전방 전개)이 이 값을 늘린다.
        /// </summary>
        public float RallyRange
        {
            get
            {
                if (Data == null)
                    return 0f;

                return CurrentStat.Range * RewardSystem.GetStatMods(Data.Type).RallyRangeMul;
            }
        }

        public override void Initialize(TowerData data, StageController stage)
        {
            base.Initialize(data, stage);

            _rallyPoint = ComputeRallyPoint();
            _rallyReady = true;

            FillEmptySlots();
        }

        /// <summary>
        /// 플레이어가 찍은 위치로 집결지를 옮긴다.
        /// 배치 가능 거리 안으로 자른 뒤 가장 가까운 경로 위로 스냅한다.
        /// 길에서 떨어진 곳에 세우면 아무것도 막지 못하므로 경로 스냅은 유지한다.
        /// </summary>
        public void SetRallyPoint(Vector3 worldPosition)
        {
            if (!_rallyReady)
                return;

            Vector3 origin = transform.position;
            origin.y = 0f;

            Vector3 target = worldPosition;
            target.y = 0f;

            _rallyPoint = ClampToPathWithinRange(origin, target);

            // 이미 나가 있는 병사들도 새 집결지로 이동한다. 자리 순번은 그대로 유지한다.
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null)
                    continue;

                _slots[i].SetRallyPoint(_rallyPoint + SpreadOffset(i));
            }

            GameLog.Info("Build", "랠리 포인트 이동");
        }

        protected override bool TryAttack()
        {
            // 보병 타워는 공격 루틴이 없다. 병사 유지는 Update에서 처리.
            return false;
        }

        protected override void Update()
        {
            if (Data == null || !_rallyReady)
                return;

            EnsureSlots();

            // 빈 자리마다 자기 대기 시간을 따로 흘린다. 동시에 죽었으면 동시에 돌아온다.
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                    continue;

                if (_slotTimers[i] > 0f)
                {
                    _slotTimers[i] -= Time.deltaTime;

                    if (_slotTimers[i] > 0f)
                        continue;
                }

                SpawnSoldierAt(i, isRespawn: true);
            }
        }

        /// <summary>자리 수를 현재 레벨의 병사 수에 맞춘다. 줄어들면 넘치는 자리의 병사를 먼저 정리한다.</summary>
        void EnsureSlots()
        {
            int count = Mathf.Max(0, CurrentStat.SoldierCount);

            if (_slots.Length == count)
                return;

            for (int i = count; i < _slots.Length; i++)
            {
                if (_slots[i] == null)
                    continue;

                _slots[i].DespawnByOwner();
            }

            Array.Resize(ref _slots, count);
            Array.Resize(ref _slotTimers, count);
        }

        /// <summary>강화 시 병사를 전부 새 스탯으로 재소환한다.</summary>
        protected override void OnLevelChanged()
        {
            base.OnLevelChanged();

            // base.Initialize 안에서 호출될 때는 집결지 계산 전이므로 건너뛴다
            if (!_rallyReady)
                return;

            DespawnAllSoldiers();
            FillEmptySlots();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            DespawnAllSoldiers();
        }

        /// <summary>빈 자리를 대기 없이 즉시 채운다 (최초 배치 / 강화 후 재배치). 충원 골드는 붙지 않는다.</summary>
        void FillEmptySlots()
        {
            EnsureSlots();

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                    continue;

                SpawnSoldierAt(i, isRespawn: false);
            }
        }

        float RespawnSeconds()
        {
            // 충원 속도 보상(C05): 속도가 오르면 시간이 줄어든다
            float speedMul = RewardSystem.GetStatMods(Data.Type).SoldierRespawnMul;
            float seconds = CurrentStat.SoldierRespawnSeconds / Mathf.Max(0.1f, speedMul);

            // 빠른 충원: 부활 대기 시간 2/4/6초 감소 (하한 3초)
            if (TryGetSkill(BranchSkillType.FastRecruit, out var recruit, out int level))
                seconds -= recruit.ValueAt(level);

            return Mathf.Max(3f, seconds);
        }

        void SpawnSoldierAt(int slot, bool isRespawn)
        {
            if (Data.SoldierPrefab == null)
            {
                // 대기 시간을 다시 걸어 둔다. 안 그러면 매 프레임 재시도하며 경고를 도배한다.
                _slotTimers[slot] = RespawnSeconds();

                GameLog.Warn("Build", $"{Data.name}: 병사 프리팹이 비어 있음");
                return;
            }

            _slotTimers[slot] = 0f;

            var stat = CurrentStat;

            // 집결지 주변으로 살짝 흩어서 배치. 자리 순번이 곧 방향이라 부활해도 같은 지점이다.
            Vector3 offset = SpreadOffset(slot);

            var go = Instantiate(Data.SoldierPrefab, _rallyPoint + offset, Quaternion.identity, transform);
            var soldier = go.GetComponent<Soldier>();

            if (soldier == null)
                soldier = go.AddComponent<Soldier>();

            // 기본값(난이도 포함)만 넘긴다. 보상 배율은 병사가 스스로 조회해 실시간 반영한다.
            float baseHp = stat.SoldierHp * Stage.SoldierHpMultiplier;

            float damageMax = Mathf.Max(stat.SoldierDamage, stat.SoldierDamageMax);

            soldier.Initialize(this, baseHp, stat.SoldierDamage, damageMax, stat.SoldierAttackInterval,
                _rallyPoint + offset, stat.Range, stat.SoldierRegenPerSecond);

            _slots[slot] = soldier;

            // 현장 보급(A01): 충원(리스폰)될 때만 골드
            if (isRespawn)
            {
                int gold = RewardSystem.SoldierRespawnGold();

                if (gold > 0)
                {
                    Stage.AddGold(gold);
                    GameLog.Info("Reward", $"현장 보급 +{gold}G");
                }
            }
        }

        void DespawnAllSoldiers()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slotTimers[i] = 0f;

                if (_slots[i] == null)
                    continue;

                _slots[i].DespawnByOwner();
                _slots[i] = null;
            }
        }

        /// <summary>병사가 죽었을 때 그 자리에만 충원 대기를 건다. 다른 자리의 대기 시간은 건드리지 않는다.</summary>
        public void NotifySoldierDied(Soldier soldier)
        {
            int slot = Array.IndexOf(_slots, soldier);

            if (slot < 0)
                return;

            _slots[slot] = null;
            _slotTimers[slot] = RespawnSeconds();
        }

        /// <summary>집결지 주변에 병사를 흩어 세우는 오프셋. 세 방향으로 120도씩 돌린다.</summary>
        static Vector3 SpreadOffset(float index)
        {
            float angle = index * 120f * Mathf.Deg2Rad;

            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SpawnSpreadRadius;
        }

        /// <summary>
        /// 타워 위치에서 가장 가까운 경로 위 지점을 병사 집결지로 삼는다.
        /// 루트가 4개이므로 그중 가장 가까운 루트 하나만 막는다 (교차 지점에 세우면 두 루트를 함께 덮는다).
        /// </summary>
        Vector3 ComputeRallyPoint()
        {
            return SnapToPath(transform.position);
        }

        /// <summary>
        /// 클릭 지점을 경로 위로 붙이고, 배치 가능 거리를 넘으면 경로를 따라 타워 쪽으로 당긴다.
        ///
        /// 먼저 자르고 나중에 스냅하면 스냅 결과가 다시 사거리 밖으로 나갈 수 있다 (경로가 타워와 나란할 때).
        /// 그래서 경로 위 진행 거리로 바꿔 놓고 그 위에서 당긴다 - 결과가 항상 경로 위에 남는다.
        /// 하한은 타워 자신의 최근접 경로 지점(= 기본 집결지)이라 언제나 유효한 답이 나온다.
        /// </summary>
        Vector3 ClampToPathWithinRange(Vector3 origin, Vector3 target)
        {
            if (Stage == null)
                return target;

            var path = Stage.NearestPath(target);

            if (path == null)
                return target;

            float towerAt = path.ClosestDistanceAlong(origin);
            float clickAt = path.ClosestDistanceAlong(target);
            float rangeSqr = RallyRange * RallyRange;

            Vector3 point = path.GetPositionAtDistance(clickAt);

            // 클릭 지점부터 타워 최근접 지점까지 이분 탐색으로 당긴다
            for (int i = 0; i < 8; i++)
            {
                if (Horizontal(point - origin).sqrMagnitude <= rangeSqr)
                    return point;

                clickAt = Mathf.Lerp(towerAt, clickAt, 0.5f);
                point = path.GetPositionAtDistance(clickAt);
            }

            return path.GetPositionAtDistance(towerAt);
        }

        static Vector3 Horizontal(Vector3 value)
        {
            value.y = 0f;

            return value;
        }

        /// <summary>가장 가까운 루트 위로 끌어다 붙인다. 루트를 못 찾으면 원래 자리를 그대로 쓴다.</summary>
        Vector3 SnapToPath(Vector3 position)
        {
            if (Stage == null)
                return position;

            Vector3 origin = position;
            origin.y = 0f;

            var path = Stage.NearestPath(origin);

            if (path == null)
                return position;

            return path.ClosestPoint(origin, out _);
        }
    }
}
