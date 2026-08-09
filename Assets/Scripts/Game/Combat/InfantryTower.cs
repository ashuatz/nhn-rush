using System.Collections.Generic;
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

        readonly List<Soldier> _soldiers = new List<Soldier>();

        Vector3 _rallyPoint;
        float _respawnTimer;
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

            SpawnMissingSoldiers(immediate: true);
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

            // 이미 나가 있는 병사들도 새 집결지로 이동한다. 소환 때와 같은 순번으로 흩어 세운다.
            int index = 0;

            foreach (var soldier in _soldiers)
            {
                if (soldier == null)
                    continue;

                soldier.SetRallyPoint(_rallyPoint + SpreadOffset(index));

                index++;
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
            if (Data == null)
                return;

            _soldiers.RemoveAll(s => s == null);

            if (_soldiers.Count >= CurrentStat.SoldierCount)
                return;

            _respawnTimer -= Time.deltaTime;

            if (_respawnTimer > 0f)
                return;

            SpawnOneSoldier(isRespawn: true);
            _respawnTimer = RespawnSeconds();
        }

        /// <summary>강화 시 병사를 전부 새 스탯으로 재소환한다.</summary>
        protected override void OnLevelChanged()
        {
            base.OnLevelChanged();

            // base.Initialize 안에서 호출될 때는 집결지 계산 전이므로 건너뛴다
            if (!_rallyReady)
                return;

            DespawnAllSoldiers();
            SpawnMissingSoldiers(immediate: true);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            DespawnAllSoldiers();
        }

        void SpawnMissingSoldiers(bool immediate)
        {
            _soldiers.RemoveAll(s => s == null);

            if (immediate)
            {
                while (_soldiers.Count < CurrentStat.SoldierCount)
                    SpawnOneSoldier(isRespawn: false);

                return;
            }

            _respawnTimer = RespawnSeconds();
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

        void SpawnOneSoldier(bool isRespawn)
        {
            if (Data.SoldierPrefab == null)
            {
                GameLog.Warn("Build", $"{Data.name}: 병사 프리팹이 비어 있음");
                return;
            }

            var stat = CurrentStat;

            // 집결지 주변으로 살짝 흩어서 배치
            Vector3 offset = SpreadOffset(_soldiers.Count);

            var go = Instantiate(Data.SoldierPrefab, _rallyPoint + offset, Quaternion.identity, transform);
            var soldier = go.GetComponent<Soldier>();

            if (soldier == null)
                soldier = go.AddComponent<Soldier>();

            // 기본값(난이도 포함)만 넘긴다. 보상 배율은 병사가 스스로 조회해 실시간 반영한다.
            float baseHp = stat.SoldierHp * Stage.SoldierHpMultiplier;

            float damageMax = Mathf.Max(stat.SoldierDamage, stat.SoldierDamageMax);

            soldier.Initialize(this, baseHp, stat.SoldierDamage, damageMax, stat.SoldierAttackInterval,
                _rallyPoint + offset, stat.Range, stat.SoldierRegenPerSecond);

            _soldiers.Add(soldier);

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
            foreach (var soldier in _soldiers)
            {
                if (soldier == null)
                    continue;

                soldier.DespawnByOwner();
            }

            _soldiers.Clear();
        }

        public void NotifySoldierDied(Soldier soldier)
        {
            _soldiers.Remove(soldier);
            _respawnTimer = RespawnSeconds();
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
