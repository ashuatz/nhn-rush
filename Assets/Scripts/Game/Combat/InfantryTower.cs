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

        public override void Initialize(TowerData data, StageController stage)
        {
            base.Initialize(data, stage);

            _rallyPoint = ComputeRallyPoint();
            _rallyReady = true;

            SpawnMissingSoldiers(immediate: true);
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
            float angle = _soldiers.Count * 120f * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SpawnSpreadRadius;

            var go = Instantiate(Data.SoldierPrefab, _rallyPoint + offset, Quaternion.identity, transform);
            var soldier = go.GetComponent<Soldier>();

            if (soldier == null)
                soldier = go.AddComponent<Soldier>();

            // 기본값(난이도 포함)만 넘긴다. 보상 배율은 병사가 스스로 조회해 실시간 반영한다.
            float baseHp = stat.SoldierHp * Stage.SoldierHpMultiplier;

            float damageMax = Mathf.Max(stat.SoldierDamage, stat.SoldierDamageMax);

            soldier.Initialize(this, baseHp, stat.SoldierDamage, damageMax, stat.SoldierAttackInterval,
                _rallyPoint + offset, stat.Range);

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

        /// <summary>타워 위치에서 가장 가까운 경로 위 지점을 병사 집결지로 삼는다.</summary>
        Vector3 ComputeRallyPoint()
        {
            var path = Stage != null ? Stage.Path : null;

            if (path == null || path.PointCount < 2)
                return transform.position;

            Vector3 origin = transform.position;
            origin.y = 0f;

            Vector3 best = path.GetPoint(0);
            float bestSqr = float.MaxValue;

            for (int i = 0; i < path.PointCount - 1; i++)
            {
                Vector3 a = path.GetPoint(i);
                Vector3 b = path.GetPoint(i + 1);
                Vector3 candidate = ClosestPointOnSegment(a, b, origin);

                float distSqr = (candidate - origin).sqrMagnitude;

                if (distSqr >= bestSqr)
                    continue;

                best = candidate;
                bestSqr = distSqr;
            }

            return best;
        }

        static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;

            if (lengthSqr < 0.0001f)
                return a;

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSqr);

            return a + ab * t;
        }
    }
}
