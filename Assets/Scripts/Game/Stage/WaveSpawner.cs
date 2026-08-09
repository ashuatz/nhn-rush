using System.Collections;
using System.Collections.Generic;
using Rush.Combat;
using Rush.Data;
using UnityEngine;

namespace Rush.Stage
{
    /// <summary>
    /// 웨이브 데이터를 받아 몬스터를 스폰한다. 스프레드시트(적 유닛 스폰 시스템).
    ///
    /// 1~6웨이브는 고정 Entries를 그대로 돌린다.
    /// 7웨이브부터는 아키타입을 뽑아 예산을 유형별로 배분하고, 배치 단위로 나눠
    /// "스폰 구간 / 총 배치 수" 간격으로 내보낸다. 경로는 배치마다 가중치로 뽑는다.
    /// 24웨이브는 정규 스폰이 끝난 뒤에도 주기적으로 추가 스폰이 계속된다.
    ///
    /// StageController가 Initialize로 참조를 주입한다 (인스펙터 순환 참조 회피).
    /// </summary>
    public class WaveSpawner : MonoBehaviour
    {
        /// <summary>경로 가중치에 쓰는 정수 난수 범위 (시트: 1~10 사이 정수 난수).</summary>
        const int RouteWeightMin = 1;
        const int RouteWeightMax = 10;

        /// <summary>스폰 단위 하나. 같은 적을 Count마리 묶어 순차로 낸다.</summary>
        class SpawnBatch
        {
            public MonsterData Monster;
            public int Count;
        }

        /// <summary>웨이브가 쓸 경로와 그 가중치. 배치마다 이 표로 경로를 뽑는다.</summary>
        class RouteLottery
        {
            public readonly List<PathRoute> Routes = new List<PathRoute>(4);
            public readonly List<int> Weights = new List<int>(4);

            public int TotalWeight;

            public PathRoute Draw()
            {
                if (Routes.Count == 0)
                    return null;

                if (TotalWeight <= 0)
                    return Routes[Random.Range(0, Routes.Count)];

                int pick = Random.Range(0, TotalWeight);

                for (int i = 0; i < Routes.Count; i++)
                {
                    pick -= Weights[i];

                    if (pick < 0)
                        return Routes[i];
                }

                return Routes[Routes.Count - 1];
            }
        }

        /// <summary>
        /// 웨이브 시작 전에 미리 뽑아 둔 구성. 어느 입구에서 나오는지를 미리 알려주기 위한 것이라
        /// 표기한 경로와 실제 스폰 경로가 어긋나지 않도록 StartWave가 이 계획을 그대로 소비한다.
        /// </summary>
        class WavePlan
        {
            public WaveData Wave;
            public int WaveNumber;

            public RouteLottery FixedLottery;
            public PathRoute BossRoute;

            public bool HasArchetype;
            public int ArchetypeIndex;
            public RouteLottery ArchetypeLottery;

            public readonly List<PathRoute> Entrances = new List<PathRoute>(4);

            public void AddEntrance(PathRoute route)
            {
                if (route == null || Entrances.Contains(route))
                    return;

                Entrances.Add(route);
            }
        }

        /// <summary>스폰 순간의 연기 연출. 에디터 셋업에서 채운다.</summary>
        [SerializeField] GameObject _spawnFx;

        StageController _stage;
        readonly List<PathRoute> _routes = new List<PathRoute>(4);
        bool _ready;
        int _runningEntries;

        /// <summary>미리 뽑아 둔 다음 웨이브 계획. 그 웨이브가 시작되면 소비하고 비운다.</summary>
        WavePlan _plan;

        static readonly List<PathRoute> EmptyRoutes = new List<PathRoute>(0);

        /// <summary>아키타입별 마지막 등장 웨이브 번호. 연속 금지/최소 간격 판정에 쓴다.</summary>
        readonly Dictionary<int, int> _archetypeLastWave = new Dictionary<int, int>(8);

        int _lastArchetypeIndex = -1;

        /// <summary>예산 계수 적용 후 남거나 넘친 금액. 다음 웨이브 예산에 가감한다 (시트: 이월).</summary>
        int _carryOver;

        /// <summary>배정된 중간 보스 종류. 판 시작 시 무작위 순열로 한 번 정한다.</summary>
        readonly List<BossArchetypeDef> _bossOrder = new List<BossArchetypeDef>(3);

        /// <summary>최종 보스가 실제로 스폰됐는지. 실패했으면 무한 스폰을 시작하지 않는다.</summary>
        bool _finalBossSpawned;

        public bool IsSpawning => _runningEntries > 0;

        /// <summary>
        /// 스폰 가능한 상태인지 반환한다. 쓸 수 있는 루트가 하나도 없으면 false.
        /// 웨이포인트가 모자란 루트는 조용히 빼지 않고 경고를 남긴 뒤 제외한다.
        /// </summary>
        public bool Initialize(StageController stage, PathRoute[] paths)
        {
            _stage = stage;
            _routes.Clear();
            _archetypeLastWave.Clear();
            _lastArchetypeIndex = -1;
            _carryOver = 0;
            _finalBossSpawned = false;
            _ready = false;
            _plan = null;

            if (stage == null)
            {
                GameLog.Warn("Wave", "StageController 참조가 없어 스포너를 초기화할 수 없음");
                return false;
            }

            if (paths == null || paths.Length == 0)
            {
                GameLog.Warn("Wave", "PathRoute 참조가 없어 스포너를 초기화할 수 없음");
                return false;
            }

            foreach (var path in paths)
            {
                if (path == null)
                {
                    GameLog.Warn("Wave", "루트 목록에 빈 항목이 있음");
                    continue;
                }

                if (path.PointCount < 2)
                {
                    GameLog.Warn("Wave", $"루트 {path.RouteId}: 웨이포인트가 {path.PointCount}개 - 최소 2개 필요");
                    continue;
                }

                _routes.Add(path);
            }

            if (_routes.Count == 0)
            {
                GameLog.Warn("Wave", "쓸 수 있는 루트가 없음");
                return false;
            }

            BuildBossOrder();

            _ready = true;

            GameLog.Info("Wave", $"루트 {_routes.Count}개로 스폰 분배");

            return true;
        }

        /// <summary>
        /// 중간 보스 3종을 무작위 순열로 세워둔다. 중복 없이 한 판에 셋이 모두 나오고 순서만 매판 달라진다.
        /// 시트(중간 보스 3종: 배치 / 경우의 수 6가지 순열).
        /// </summary>
        void BuildBossOrder()
        {
            _bossOrder.Clear();

            var defs = _stage.Data.BossArchetypes;

            if (defs == null || defs.Length == 0)
            {
                GameLog.Warn("Wave", "중간 보스 종류 데이터가 없음 - 슬롯 기본 스탯으로 등장");
                return;
            }

            foreach (var def in defs)
            {
                if (def == null)
                    continue;

                _bossOrder.Add(def);
            }

            for (int i = _bossOrder.Count - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);

                (_bossOrder[i], _bossOrder[swap]) = (_bossOrder[swap], _bossOrder[i]);
            }

            var names = new string[_bossOrder.Count];

            for (int i = 0; i < _bossOrder.Count; i++)
                names[i] = _bossOrder[i].DisplayName;

            GameLog.Info("Wave", $"중간 보스 순서: {string.Join(" -> ", names)}");
        }

        /// <summary>
        /// 웨이브가 쓸 루트 목록. RouteIds가 비어 있으면 전체 루트를 쓴다.
        /// 지정한 ID를 하나도 못 찾으면 웨이브를 통째로 날리는 대신 전체 루트로 되돌린다.
        /// </summary>
        List<PathRoute> ResolveRoutes(WaveData wave)
        {
            if (wave.RouteIds == null || wave.RouteIds.Length == 0)
                return _routes;

            var filtered = new List<PathRoute>(wave.RouteIds.Length);

            foreach (var id in wave.RouteIds)
            {
                PathRoute found = null;

                foreach (var route in _routes)
                {
                    if (!string.Equals(route.RouteId, id, System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    found = route;
                    break;
                }

                if (found == null)
                {
                    GameLog.Warn("Wave", $"루트 {id}를 찾지 못해 제외");
                    continue;
                }

                if (!filtered.Contains(found))
                    filtered.Add(found);
            }

            if (filtered.Count == 0)
            {
                GameLog.Warn("Wave", "지정한 루트를 하나도 못 찾아 전체 루트로 진행");
                return _routes;
            }

            return filtered;
        }

        /// <summary>
        /// 고정 구성(1~6웨이브)이 쓸 경로 추첨표. 시트(고정 구간 경로)의 가중치를 그대로 쓴다.
        /// 가중치는 순서가 아니라 경로 ID로 짝짓는다. 순서로 맞추면 ResolveRoutes가 전체 경로로
        /// 폴백했을 때 엉뚱한 경로에 가중치가 붙는다.
        /// 가중치가 없거나 ID를 하나도 못 찾으면 주어진 경로에 균등 분배한다.
        /// </summary>
        static RouteLottery BuildFixedLottery(WaveData wave, List<PathRoute> routes)
        {
            var lottery = new RouteLottery();

            bool useWeights = wave.RouteIds != null
                && wave.RouteWeights != null
                && wave.RouteWeights.Length == wave.RouteIds.Length;

            if (useWeights)
            {
                for (int i = 0; i < wave.RouteIds.Length; i++)
                {
                    var route = FindRoute(routes, wave.RouteIds[i]);

                    if (route == null)
                        continue;

                    int weight = wave.RouteWeights[i];

                    // 가중치 0인 경로는 실제로 뽑히지 않는다. 입구 표기가 어긋나므로 표에서 뺀다.
                    if (weight <= 0)
                        continue;

                    lottery.Routes.Add(route);
                    lottery.Weights.Add(weight);
                    lottery.TotalWeight += weight;
                }
            }

            if (lottery.Routes.Count > 0)
                return lottery;

            foreach (var route in routes)
            {
                lottery.Routes.Add(route);
                lottery.Weights.Add(1);
                lottery.TotalWeight += 1;
            }

            return lottery;
        }

        static PathRoute FindRoute(List<PathRoute> routes, string routeId)
        {
            if (string.IsNullOrEmpty(routeId))
                return null;

            foreach (var route in routes)
            {
                if (string.Equals(route.RouteId, routeId, System.StringComparison.OrdinalIgnoreCase))
                    return route;
            }

            return null;
        }

        /// <summary>
        /// 보스가 탈 단일 경로. BossRouteId가 있으면 그 경로로 고정하고(시트: 6웨이브는 A1 고정),
        /// 없으면 이 웨이브가 쓸 수 있는 경로 중 하나를 무작위로 뽑는다.
        /// </summary>
        static PathRoute ResolveBossRoute(WaveData wave, List<PathRoute> routes)
        {
            if (routes.Count == 0)
                return null;

            if (string.IsNullOrEmpty(wave.BossRouteId))
                return routes[Random.Range(0, routes.Count)];

            var fixedRoute = FindRoute(routes, wave.BossRouteId);

            if (fixedRoute != null)
                return fixedRoute;

            GameLog.Warn("Wave", $"보스 고정 경로 {wave.BossRouteId}를 찾지 못해 무작위로 뽑음");

            return routes[Random.Range(0, routes.Count)];
        }

        /// <summary>
        /// 다음 웨이브가 쓸 구성(아키타입/경로)을 미리 뽑아 두고 입구 목록을 돌려준다.
        /// UI가 "어느 입구에서 나오는지"를 웨이브 시작 전에 표기하기 위한 것이다.
        /// 계획은 그 웨이브가 시작될 때 그대로 쓰이므로 표기와 실제가 어긋나지 않는다.
        /// </summary>
        public List<PathRoute> PlanWave(WaveData wave, int waveNumber)
        {
            _plan = null;

            if (!_ready || wave == null)
                return EmptyRoutes;

            var plan = new WavePlan { Wave = wave, WaveNumber = waveNumber };
            var routes = ResolveRoutes(wave);
            bool hasFixed = wave.Entries != null && wave.Entries.Length > 0;

            if (hasFixed)
            {
                plan.FixedLottery = BuildFixedLottery(wave, routes);
                plan.BossRoute = ResolveBossRoute(wave, routes);

                foreach (var route in plan.FixedLottery.Routes)
                    plan.AddEntrance(route);

                // 보스는 단일 경로로 고정되므로 추첨표에 없더라도 입구로 잡아 준다
                if (HasBossEntry(wave))
                    plan.AddEntrance(plan.BossRoute);
            }

            if (!hasFixed || wave.IsBossWave || wave.IsFinalWave)
            {
                bool fixedRandomPool = wave.IsFinalWave;

                plan.HasArchetype = true;
                plan.ArchetypeIndex = fixedRandomPool ? RandomPoolArchetypeIndex() : PickArchetype(waveNumber);
                plan.ArchetypeLottery = BuildRouteLottery(GetArchetype(plan.ArchetypeIndex), routes);

                foreach (var route in plan.ArchetypeLottery.Routes)
                    plan.AddEntrance(route);
            }

            _plan = plan;

            return plan.Entrances;
        }

        /// <summary>고정 구성에 보스가 들어 있는지. 보스 전용 경로를 입구로 잡을지 판단한다.</summary>
        static bool HasBossEntry(WaveData wave)
        {
            if (wave.Entries == null)
                return false;

            foreach (var entry in wave.Entries)
            {
                if (entry != null && entry.Monster != null && entry.Monster.IsBoss)
                    return true;
            }

            return false;
        }

        public void StartWave(WaveData wave, float enemyHpMultiplier, int waveNumber)
        {
            if (!_ready)
            {
                GameLog.Warn("Wave", "스포너가 초기화되지 않아 웨이브를 시작할 수 없음");
                return;
            }

            if (wave == null)
            {
                GameLog.Warn("Wave", "빈 웨이브 데이터 - 스폰 생략");
                return;
            }

            float statMultiplier = Mathf.Max(0.01f, wave.StatMultiplier);
            bool hasFixed = wave.Entries != null && wave.Entries.Length > 0;
            bool anySpawn = false;

            // 웨이브별 루트 제한. 조기소환으로 웨이브가 겹쳐도 서로 영향이 없도록 코루틴에 넘긴다.
            var routes = ResolveRoutes(wave);

            if (routes != _routes)
                GameLog.Info("Wave", $"루트 제한: {string.Join("/", wave.RouteIds)}");

            // 미리 표기해 둔 계획이 있으면 그대로 쓴다 (표기한 입구와 실제 스폰 입구를 맞춘다)
            if (_plan == null || _plan.Wave != wave || _plan.WaveNumber != waveNumber)
                PlanWave(wave, waveNumber);

            var plan = _plan;
            _plan = null;

            if (hasFixed)
            {
                // 고정 구성은 배치 규칙을 타지 않는다. 경로는 시트의 고정 구간 가중치로 뽑는다.
                var lottery = plan != null ? plan.FixedLottery : BuildFixedLottery(wave, routes);
                var bossRoute = plan != null ? plan.BossRoute : ResolveBossRoute(wave, routes);

                foreach (var entry in wave.Entries)
                {
                    StartCoroutine(RunFixedEntry(entry, lottery, bossRoute, enemyHpMultiplier, statMultiplier, waveNumber));
                    anySpawn = true;
                }
            }

            // 아키타입 배치 구성: 고정 구성이 없는 웨이브(7~)는 예산 전체,
            // 보스 웨이브는 보스 단가를 뺀 잔여 예산을 채운다. 완전 고정 구간(1~5)은 채우지 않는다.
            if (!hasFixed || wave.IsBossWave || wave.IsFinalWave)
            {
                if (StartArchetypeSpawn(wave, waveNumber, statMultiplier, enemyHpMultiplier, false, plan))
                    anySpawn = true;
            }

            // 24웨이브는 정규 스폰이 끝난 뒤에도 추가 스폰이 계속된다
            if (wave.IsFinalWave)
            {
                StartCoroutine(RunEndlessSpawn(wave, waveNumber, statMultiplier, enemyHpMultiplier));
                anySpawn = true;
            }

            if (!anySpawn)
                GameLog.Warn("Wave", "스폰할 구성이 없음 - 고정 구성과 아키타입 모두 비어 있음");
        }

        public void StopAll()
        {
            StopAllCoroutines();
            _runningEntries = 0;
        }

        // ---------- 예산 ----------

        /// <summary>
        /// 웨이브 예산에서 고정 구성 단가를 뺀 잔여 예산.
        /// 단가는 실제 지급 골드(Monster.ScaledGold)와 같은 정수 계산을 쓴다.
        /// </summary>
        static int LeftoverBudget(WaveData wave, float statMultiplier)
        {
            int spent = 0;

            if (wave.Entries != null)
            {
                foreach (var entry in wave.Entries)
                {
                    if (entry == null || entry.Monster == null)
                        continue;

                    spent += Monster.ScaledGold(entry.Monster, statMultiplier) * entry.Count;
                }
            }

            return Mathf.Max(0, wave.Budget - spent);
        }

        // ---------- 아키타입 배치 스폰 ----------

        /// <summary>
        /// 아키타입을 뽑아 배치 계획을 세우고 스폰 코루틴을 띄운다.
        ///
        /// 24웨이브(정규 스폰과 추가 스폰 모두)는 시트가 "짬 아키타입, 6종 전부"로 못박아 두었으므로
        /// 아키타입을 추첨하지 않고 예산 계수도 적용하지 않는다. 등장 이력/이월도 건드리지 않는다.
        /// </summary>
        bool StartArchetypeSpawn(WaveData wave, int waveNumber, float statMultiplier,
            float enemyHpMultiplier, bool endless, WavePlan plan = null)
        {
            bool fixedRandomPool = endless || wave.IsFinalWave;

            int budget = endless
                ? Mathf.RoundToInt(wave.Budget * _stage.Data.EndlessBudgetFraction)
                : LeftoverBudget(wave, statMultiplier);

            if (budget <= 0)
                return false;

            // 미리 뽑아 둔 계획이 있으면 아키타입도 그때 정한 것을 쓴다 (입구 표기와 어긋나지 않게)
            bool planned = plan != null && plan.HasArchetype;

            int archetypeIndex = planned
                ? plan.ArchetypeIndex
                : fixedRandomPool ? RandomPoolArchetypeIndex() : PickArchetype(waveNumber);

            var archetype = GetArchetype(archetypeIndex);

            // 예산 계수는 아키타입 추첨의 일부다. 짬 고정 구간에 또 곱하면 시트의 추가 예산 50%가 42.5%가 된다.
            float scale = 1f;

            if (!fixedRandomPool && archetype != null)
                scale = archetype.BudgetScale;

            int pool = Mathf.RoundToInt(budget * scale);

            if (!fixedRandomPool)
                pool += _carryOver;

            if (pool <= 0)
            {
                if (!fixedRandomPool)
                    _carryOver = pool;

                return false;
            }

            var batches = new List<SpawnBatch>(32);
            int spent = BuildBatches(archetype, pool, statMultiplier, batches);

            if (batches.Count == 0)
            {
                // 가장 싼 적조차 못 사는 예산은 다음 웨이브로 넘긴다
                if (!fixedRandomPool)
                    _carryOver = pool;

                return false;
            }

            if (!fixedRandomPool)
            {
                _carryOver = pool - spent;
                _lastArchetypeIndex = archetypeIndex;
                _archetypeLastWave[archetypeIndex] = waveNumber;
            }

            float interval = ResolveBatchInterval(batches);

            var lottery = planned && plan.ArchetypeLottery != null
                ? plan.ArchetypeLottery
                : BuildRouteLottery(archetype, ResolveRoutes(wave));

            string label = archetype != null ? archetype.Name : "무작위";
            GameLog.Info("Wave", $"웨이브 {waveNumber} {label}: 예산 {pool} 사용 {spent}, 배치 {batches.Count}개, 간격 {interval:F2}초, 경로 {lottery.Routes.Count}개");

            StartCoroutine(RunBatches(batches, lottery, interval, enemyHpMultiplier, statMultiplier, waveNumber));

            return true;
        }

        WaveArchetypeDef GetArchetype(int index)
        {
            var defs = _stage.Data.Archetypes;

            if (defs == null || index < 0 || index >= defs.Length)
                return null;

            return defs[index];
        }

        /// <summary>구성원이 없는(무작위 풀을 쓰는) 아키타입의 인덱스. 없으면 -1.</summary>
        int RandomPoolArchetypeIndex()
        {
            var defs = _stage.Data.Archetypes;

            if (defs == null)
                return -1;

            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i] != null && defs[i].UsesRandomPool)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// 웨이브 아키타입 추첨. 제약을 전부 통과한 후보로 먼저 뽑고, 후보가 없으면
        /// 연속 금지만 풀어 다시 뽑는다 (구성이 조용히 비는 것보다는 같은 아키타입이 이어지는 편이 낫다).
        /// 그래도 없으면 -1을 반환하고 호출부가 무작위 풀로 되돌린다.
        /// </summary>
        int PickArchetype(int waveNumber)
        {
            var defs = _stage.Data.Archetypes;

            if (defs == null || defs.Length == 0)
                return -1;

            int picked = PickArchetypeWithin(waveNumber, true);

            if (picked >= 0)
                return picked;

            picked = PickArchetypeWithin(waveNumber, false);

            if (picked >= 0)
            {
                GameLog.Warn("Wave", $"웨이브 {waveNumber}: 제약을 통과한 아키타입이 없어 연속 금지를 풀고 뽑음");
                return picked;
            }

            GameLog.Warn("Wave", $"웨이브 {waveNumber}: 뽑을 아키타입이 없어 무작위 풀로 진행");

            return RandomPoolArchetypeIndex();
        }

        /// <summary>구간 확률 가중 추첨. 후보가 없으면 -1.</summary>
        int PickArchetypeWithin(int waveNumber, bool banRepeat)
        {
            var defs = _stage.Data.Archetypes;
            int band = ResolveBand(waveNumber);
            bool afterBoss = IsAfterBossWave(waveNumber);

            float total = 0f;

            for (int i = 0; i < defs.Length; i++)
            {
                if (!IsArchetypeAllowed(defs[i], i, waveNumber, band, afterBoss, banRepeat))
                    continue;

                total += BandChance(defs[i], band);
            }

            if (total <= 0f)
                return -1;

            float pick = Random.Range(0f, total);

            for (int i = 0; i < defs.Length; i++)
            {
                if (!IsArchetypeAllowed(defs[i], i, waveNumber, band, afterBoss, banRepeat))
                    continue;

                pick -= BandChance(defs[i], band);

                if (pick <= 0f)
                    return i;
            }

            return -1;
        }

        /// <summary>중간 보스 웨이브 직후인지 (7/13/19). 시트: 짬 또는 개떼 2만 배치.</summary>
        public static bool IsAfterBossWave(int waveNumber)
        {
            return waveNumber == 7 || waveNumber == 13 || waveNumber == 19;
        }

        public static bool IsArchetypeAllowed(WaveArchetypeDef def, int waveNumber, int band, bool afterBoss)
        {
            if (def == null)
                return false;

            if (BandChance(def, band) <= 0f)
                return false;

            if (def.MinWave > 0 && waveNumber < def.MinWave)
                return false;

            if (afterBoss && !def.AllowedAfterBoss)
                return false;

            return true;
        }

        bool IsArchetypeAllowed(WaveArchetypeDef def, int index, int waveNumber, int band,
            bool afterBoss, bool banRepeat)
        {
            if (!IsArchetypeAllowed(def, waveNumber, band, afterBoss))
                return false;

            // 연속 금지: 같은 아키타입이 두 웨이브 연속으로 나오지 않는다
            if (banRepeat && index == _lastArchetypeIndex)
                return false;

            if (banRepeat && def.MinGap > 0 && _archetypeLastWave.TryGetValue(index, out int lastWave))
            {
                if (waveNumber - lastWave < def.MinGap)
                    return false;
            }

            return true;
        }

        static float BandChance(WaveArchetypeDef def, int band)
        {
            if (def == null || def.BandChance == null || def.BandChance.Length == 0)
                return 0f;

            return def.BandChance[Mathf.Clamp(band, 0, def.BandChance.Length - 1)];
        }

        /// <summary>등장 확률 구간. 0 = 7~12, 1 = 13~18, 2 = 19 이상.</summary>
        static int ResolveBand(int waveNumber)
        {
            if (waveNumber <= 12)
                return 0;

            if (waveNumber <= 18)
                return 1;

            return 2;
        }

        /// <summary>
        /// 예산을 구성원별로 배분해 배치 목록을 만든다. 반환값은 실제로 쓴 금액.
        /// 배분 뒤 남은 잔돈은 가장 싼 구성원으로 한 번 더 채워 예산 상한을 최대한 맞춘다.
        /// </summary>
        int BuildBatches(WaveArchetypeDef archetype, int pool, float statMultiplier, List<SpawnBatch> batches)
        {
            var members = ResolveMembers(archetype);

            if (members.Count == 0)
                return 0;

            int spent = 0;
            var counts = new int[members.Count];

            for (int i = 0; i < members.Count; i++)
            {
                var monster = members[i].Monster;

                if (monster == null)
                    continue;

                int unitCost = Mathf.Max(1, Monster.ScaledGold(monster, statMultiplier));
                int share = Mathf.FloorToInt(pool * members[i].Share);

                counts[i] = share / unitCost;
                spent += counts[i] * unitCost;

                // 배분액이 단가보다 적으면 그 구성원은 조용히 사라진다. 밸런스 사고라 로그로 남긴다.
                if (counts[i] == 0 && members[i].Share > 0f)
                    GameLog.Warn("Wave", $"{monster.DisplayName}: 배분 {share}이 단가 {unitCost}보다 적어 0마리");
            }

            // 잔돈 소진: 남은 금액으로 살 수 있는 가장 싼 구성원을 계속 채운다
            int cheapest = CheapestMember(members, statMultiplier, out int cheapestCost);

            if (cheapest >= 0 && cheapestCost > 0)
            {
                int remaining = pool - spent;
                int extra = remaining / cheapestCost;

                if (extra > 0)
                {
                    counts[cheapest] += extra;
                    spent += extra * cheapestCost;
                }
            }

            for (int i = 0; i < members.Count; i++)
            {
                if (counts[i] <= 0)
                    continue;

                AppendBatches(members[i].Monster, counts[i], batches);
            }

            Shuffle(batches);

            return spent;
        }

        /// <summary>구성원 목록. 지정이 없는 아키타입(짬)은 무작위 풀을 균등 배분으로 쓴다.</summary>
        List<ArchetypeMember> ResolveMembers(WaveArchetypeDef archetype)
        {
            var result = new List<ArchetypeMember>(8);

            if (archetype != null && !archetype.UsesRandomPool)
            {
                foreach (var member in archetype.Members)
                {
                    if (member == null || member.Monster == null)
                        continue;

                    result.Add(member);
                }

                return result;
            }

            var pool = _stage.Data.RandomPool;

            if (pool == null)
                return result;

            int valid = 0;

            foreach (var monster in pool)
            {
                if (monster != null)
                    valid++;
            }

            if (valid == 0)
                return result;

            float share = 1f / valid;

            foreach (var monster in pool)
            {
                if (monster == null)
                    continue;

                result.Add(new ArchetypeMember { Monster = monster, Share = share });
            }

            return result;
        }

        static int CheapestMember(List<ArchetypeMember> members, float statMultiplier, out int cost)
        {
            int best = -1;

            cost = int.MaxValue;

            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].Monster == null)
                    continue;

                int unitCost = Mathf.Max(1, Monster.ScaledGold(members[i].Monster, statMultiplier));

                if (unitCost >= cost)
                    continue;

                best = i;
                cost = unitCost;
            }

            if (best < 0)
                cost = 0;

            return best;
        }

        /// <summary>마릿수를 배치 크기로 나눠 배치 목록에 넣는다. 마지막 배치가 나머지를 갖는다.</summary>
        static void AppendBatches(MonsterData monster, int count, List<SpawnBatch> batches)
        {
            int size = Mathf.Max(1, monster.BatchSize);

            while (count > 0)
            {
                int take = Mathf.Min(size, count);

                batches.Add(new SpawnBatch { Monster = monster, Count = take });
                count -= take;
            }
        }

        static void Shuffle(List<SpawnBatch> batches)
        {
            for (int i = batches.Count - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);

                (batches[i], batches[swap]) = (batches[swap], batches[i]);
            }
        }

        /// <summary>
        /// 배치 간 간격 = 스폰 구간 / 총 배치 수. 하한보다 좁으면 배치 크기를 2배로 늘려 재계산한다
        /// (마법사/백인대장 제외, 최대 MaxBatchGrowth회). 시트(간격 규칙 / 자동 조정).
        /// </summary>
        float ResolveBatchInterval(List<SpawnBatch> batches)
        {
            var data = _stage.Data;
            float window = Mathf.Max(1f, data.SpawnWindow);
            float interval = window / batches.Count;

            for (int growth = 1; growth <= data.MaxBatchGrowth; growth++)
            {
                if (interval >= data.MinBatchInterval)
                    return interval;

                if (!TryGrowBatches(batches))
                    return interval;

                interval = window / batches.Count;
            }

            return interval;
        }

        /// <summary>
        /// 배치 크기를 2배로 묶어 배치 수를 줄인다. 늘릴 수 있는 적이 없으면 false.
        /// 같은 적의 배치를 두 개씩 합치는 방식이라 총 마릿수는 그대로다.
        /// </summary>
        static bool TryGrowBatches(List<SpawnBatch> batches)
        {
            var merged = new List<SpawnBatch>(batches.Count);
            var pending = new Dictionary<MonsterData, SpawnBatch>();
            bool grew = false;

            foreach (var batch in batches)
            {
                if (!batch.Monster.AllowBatchGrowth)
                {
                    merged.Add(batch);
                    continue;
                }

                if (pending.TryGetValue(batch.Monster, out var open))
                {
                    open.Count += batch.Count;
                    pending.Remove(batch.Monster);
                    grew = true;
                    continue;
                }

                pending[batch.Monster] = batch;
                merged.Add(batch);
            }

            if (!grew)
                return false;

            batches.Clear();
            batches.AddRange(merged);

            Shuffle(batches);

            return true;
        }

        // ---------- 경로 가중치 ----------

        /// <summary>
        /// 사용할 경로 수를 확률표로 뽑고, 그만큼 무작위로 골라 1~10 정수 난수를 가중치로 준다.
        /// 시트(경로 가중치 - 7~23웨이브).
        /// </summary>
        RouteLottery BuildRouteLottery(WaveArchetypeDef archetype, List<PathRoute> available)
        {
            var lottery = new RouteLottery();

            if (available.Count == 0)
                return lottery;

            int count = PickRouteCount(archetype, available.Count);

            var pickable = new List<PathRoute>(available);

            for (int i = 0; i < count && pickable.Count > 0; i++)
            {
                int index = Random.Range(0, pickable.Count);
                int weight = Random.Range(RouteWeightMin, RouteWeightMax + 1);

                lottery.Routes.Add(pickable[index]);
                lottery.Weights.Add(weight);
                lottery.TotalWeight += weight;

                pickable.RemoveAt(index);
            }

            return lottery;
        }

        int PickRouteCount(WaveArchetypeDef archetype, int maxCount)
        {
            var chances = archetype != null ? archetype.RouteCountChance : null;

            if (chances == null || chances.Length == 0)
                return maxCount;

            float total = 0f;

            for (int i = 0; i < chances.Length && i < maxCount; i++)
                total += chances[i];

            if (total <= 0f)
                return maxCount;

            float pick = Random.Range(0f, total);

            for (int i = 0; i < chances.Length && i < maxCount; i++)
            {
                pick -= chances[i];

                if (pick <= 0f)
                    return i + 1;
            }

            return maxCount;
        }

        // ---------- 코루틴 ----------

        IEnumerator RunBatches(List<SpawnBatch> batches, RouteLottery lottery, float interval,
            float enemyHpMultiplier, float statMultiplier, int waveNumber)
        {
            _runningEntries++;

            float baseInner = Mathf.Max(0f, _stage.Data.BatchInnerInterval);

            for (int i = 0; i < batches.Count; i++)
            {
                var batch = batches[i];
                var route = lottery.Draw();

                if (route == null)
                    break;

                // 배치 내부 스폰이 배치 간 간격보다 길어지면 전체 스폰이 스폰 구간을 넘는다.
                // 자동 조정으로 커진 배치가 그 상황을 만들므로 내부 간격을 간격 안에 눌러 담는다.
                float inner = baseInner;
                int gaps = Mathf.Max(0, batch.Count - 1);

                if (gaps > 0)
                    inner = Mathf.Min(baseInner, interval / gaps);

                for (int n = 0; n < batch.Count; n++)
                {
                    Spawn(batch.Monster, route, enemyHpMultiplier, statMultiplier, waveNumber);

                    if (n < batch.Count - 1 && inner > 0f)
                        yield return new WaitForSeconds(inner);
                }

                if (i >= batches.Count - 1)
                    continue;

                // 배치 내부 스폰에 쓴 시간을 빼서 전체 길이가 스폰 구간을 넘지 않게 한다
                float wait = interval - inner * gaps;

                if (wait > 0f)
                    yield return new WaitForSeconds(wait);
            }

            _runningEntries--;
        }

        /// <summary>
        /// 24웨이브 추가 스폰. 주기마다 정규 예산의 일부를 짬 구성으로 내보내고
        /// 적 스탯 배수를 누적해서 올린다. 최종 보스를 처치하면 StageController가 StopAll로 끊는다.
        /// </summary>
        IEnumerator RunEndlessSpawn(WaveData wave, int waveNumber, float baseMultiplier, float enemyHpMultiplier)
        {
            _runningEntries++;

            var data = _stage.Data;
            float period = Mathf.Max(1f, data.EndlessSpawnInterval);

            // 첫 추가 스폰은 정규 스폰 구간이 끝난 뒤부터
            yield return new WaitForSeconds(Mathf.Max(1f, data.SpawnWindow));

            // 최종 보스가 못 나왔다면 무한 스폰을 시작하지 않는다.
            // 처치로만 끝나는 웨이브인데 보스가 없으면 승리도 패배도 나올 수 없다.
            if (!_finalBossSpawned)
            {
                GameLog.Warn("Wave", "최종 보스가 스폰되지 않아 추가 스폰을 중단함 - 남은 적을 정리하면 승리 판정으로 넘어간다");

                _runningEntries--;
                yield break;
            }

            for (int step = 1; ; step++)
            {
                yield return new WaitForSeconds(period);

                float multiplier = baseMultiplier * (1f + data.EndlessStatStep * step);

                GameLog.Info("Wave", $"최종 보스전 추가 스폰 {step}회 - 스탯 배수 {multiplier:F2}");

                StartArchetypeSpawn(wave, waveNumber, multiplier, enemyHpMultiplier, true);
            }
        }

        /// <summary>고정 구성 항목 하나. 배치 규칙 없이 데이터에 적힌 간격으로 낸다.</summary>
        IEnumerator RunFixedEntry(SpawnEntry entry, RouteLottery lottery, PathRoute bossRoute,
            float enemyHpMultiplier, float statMultiplier, int waveNumber)
        {
            if (entry == null || entry.Monster == null)
            {
                GameLog.Warn("Wave", "스폰 항목에 몬스터 데이터가 없음");
                yield break;
            }

            _runningEntries++;

            if (entry.StartDelay > 0f)
                yield return new WaitForSeconds(entry.StartDelay);

            // 보스만 단일 경로로 고정하고, 잡졸은 매번 가중치로 다시 뽑는다
            PathRoute lockedRoute = entry.Monster.IsBoss ? bossRoute : null;

            for (int i = 0; i < entry.Count; i++)
            {
                var route = lockedRoute != null ? lockedRoute : lottery.Draw();

                if (route == null)
                    break;

                bool spawned = Spawn(entry.Monster, route, enemyHpMultiplier, statMultiplier, waveNumber);

                // 최종 보스가 못 나왔으면 무한 스폰을 시작하지 않는다 (승리 조건이 영구히 막힌다)
                if (spawned && entry.Monster.IsFinalBoss)
                    _finalBossSpawned = true;

                if (i < entry.Count - 1)
                    yield return new WaitForSeconds(entry.Interval);
            }

            _runningEntries--;
        }

        /// <summary>몬스터 1마리 스폰. 프리팹이 없어 스폰하지 못하면 false.</summary>
        bool Spawn(MonsterData data, PathRoute route, float enemyHpMultiplier, float statMultiplier,
            int waveNumber)
        {
            if (data.Prefab == null)
            {
                GameLog.Warn("Wave", $"{data.name}: 프리팹이 비어 있어 스폰 불가");
                return false;
            }

            var go = Instantiate(data.Prefab, route.GetPoint(0), Quaternion.identity, transform);
            var monster = go.GetComponent<Monster>();

            if (monster == null)
            {
                GameLog.Warn("Wave", $"{data.name}: 프리팹에 Monster 컴포넌트가 없어 추가함");
                monster = go.AddComponent<Monster>();
            }

            monster.Initialize(data, route, enemyHpMultiplier, statMultiplier,
                _stage.HandleMonsterDied, _stage.HandleMonsterReachedExit);

            // 중간 보스는 판 시작 시 뽑은 순열에서 이 웨이브에 배정된 종류를 입힌다
            if (data.IsBoss && !data.IsFinalBoss)
                monster.ApplyBossArchetype(BossArchetypeFor(waveNumber));

            // 연기는 Initialize가 확정한 실제 위치에 터뜨린다 (몬스터가 레인 오프셋만큼 옆으로 비켜 선다)
            Rush.Fx.OneShotFx.Spawn(_spawnFx, monster.transform.position);

            return true;
        }

        /// <summary>
        /// 이 웨이브에 배정된 중간 보스 종류. 스폰 횟수가 아니라 "몇 번째 보스 웨이브인지"로 고른다.
        /// 스폰 실패나 보스 항목 수 변화가 이후 웨이브 배정을 밀어버리지 않게 하려는 것이다.
        /// </summary>
        BossArchetypeDef BossArchetypeFor(int waveNumber)
        {
            if (_bossOrder.Count == 0)
                return null;

            var waves = _stage.Data.Waves;
            int ordinal = 0;

            for (int i = 0; i < waves.Length && i < waveNumber; i++)
            {
                if (waves[i] != null && waves[i].IsBossWave)
                    ordinal++;
            }

            if (ordinal <= 0)
                return _bossOrder[0];

            return _bossOrder[(ordinal - 1) % _bossOrder.Count];
        }
    }
}
