using System.Collections;
using System.Collections.Generic;
using Rush.Combat;
using Rush.Data;
using UnityEngine;

namespace Rush.Stage
{
    /// <summary>
    /// 웨이브 데이터를 받아 몬스터를 순차 스폰한다.
    /// 고정 Entries를 먼저 코루틴으로 돌리고, 남은 예산은 RandomPool에서 무작위 구성으로 채운다.
    /// 무작위 단가 = 기본 킬 보상 x 웨이브 배수 (스프레드시트: 웨이브 예산 = 벌 수 있는 최대 골드).
    /// StageController가 Initialize로 참조를 주입한다 (인스펙터 순환 참조 회피).
    /// </summary>
    public class WaveSpawner : MonoBehaviour
    {
        /// <summary>스폰 순간의 연기 연출. 에디터 셋업에서 채운다.</summary>
        [SerializeField] GameObject _spawnFx;

        StageController _stage;
        PathRoute _path;
        bool _ready;
        int _runningEntries;

        public bool IsSpawning => _runningEntries > 0;

        /// <summary>스폰 가능한 상태인지 반환한다. 경로가 없거나 웨이포인트가 부족하면 false.</summary>
        public bool Initialize(StageController stage, PathRoute path)
        {
            _stage = stage;
            _path = path;
            _ready = false;

            if (stage == null)
            {
                GameLog.Warn("Wave", "StageController 참조가 없어 스포너를 초기화할 수 없음");
                return false;
            }

            if (path == null)
            {
                GameLog.Warn("Wave", "PathRoute 참조가 없어 스포너를 초기화할 수 없음");
                return false;
            }

            if (path.PointCount < 2)
            {
                GameLog.Warn("Wave", $"경로 웨이포인트가 {path.PointCount}개 - 최소 2개 필요");
                return false;
            }

            _ready = true;

            return true;
        }

        public void StartWave(WaveData wave, float enemyHpMultiplier)
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

            if (hasFixed)
            {
                foreach (var entry in wave.Entries)
                {
                    StartCoroutine(RunEntry(entry, enemyHpMultiplier, statMultiplier));
                    anySpawn = true;
                }
            }

            // 무작위 구성: 고정 구성이 없는 웨이브(7~)는 예산 전체,
            // 보스 웨이브는 보스 단가를 뺀 잔여 예산을 채운다. 완전 고정 구간(1~6 일반)은 채우지 않는다.
            if (!hasFixed || wave.IsBossWave)
            {
                // 조기소환으로 웨이브가 겹칠 수 있으므로 코루틴마다 자기 스트림을 갖는다
                var stream = BuildRandomStream(wave, statMultiplier);

                if (stream != null)
                {
                    StartCoroutine(RunRandomStream(stream, enemyHpMultiplier, statMultiplier));
                    anySpawn = true;
                }
            }

            if (!anySpawn)
                GameLog.Warn("Wave", "스폰할 구성이 없음 - 고정 구성과 무작위 풀 모두 비어 있음");
        }

        public void StopAll()
        {
            StopAllCoroutines();
            _runningEntries = 0;
        }

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

        /// <summary>잔여 예산을 무작위 풀에서 소진한 스폰 목록. 뽑을 수 있는 적이 없으면 null.</summary>
        List<MonsterData> BuildRandomStream(WaveData wave, float statMultiplier)
        {
            var pool = _stage.Data.RandomPool;
            int remaining = LeftoverBudget(wave, statMultiplier);

            if (remaining <= 0 || pool == null || pool.Length == 0)
                return null;

            var stream = new List<MonsterData>(64);

            // 무한 루프 방지 상한 (예산/최저단가보다 넉넉하게)
            for (int guard = 0; guard < 4096; guard++)
            {
                MonsterData picked = PickAffordable(pool, remaining, statMultiplier);

                if (picked == null)
                    break;

                stream.Add(picked);
                remaining -= Monster.ScaledGold(picked, statMultiplier);
            }

            if (stream.Count == 0)
                return null;

            return stream;
        }

        static MonsterData PickAffordable(MonsterData[] pool, int remaining, float statMultiplier)
        {
            int affordable = 0;

            foreach (var monster in pool)
            {
                if (monster == null)
                    continue;

                if (Monster.ScaledGold(monster, statMultiplier) <= remaining)
                    affordable++;
            }

            if (affordable == 0)
                return null;

            int pick = Random.Range(0, affordable);

            foreach (var monster in pool)
            {
                if (monster == null)
                    continue;

                if (Monster.ScaledGold(monster, statMultiplier) > remaining)
                    continue;

                if (pick == 0)
                    return monster;

                pick--;
            }

            return null;
        }

        IEnumerator RunRandomStream(List<MonsterData> stream, float enemyHpMultiplier, float statMultiplier)
        {
            _runningEntries++;

            // 후반 웨이브는 마릿수가 많아 스트림이 웨이브 간격(30초)을 넘지 않게 간격을 줄인다
            float interval = Mathf.Max(0.1f, _stage.Data.RandomSpawnInterval);

            if (stream.Count > 1)
                interval = Mathf.Clamp(28f / stream.Count, 0.08f, interval);

            for (int i = 0; i < stream.Count; i++)
            {
                Spawn(stream[i], enemyHpMultiplier, statMultiplier);

                if (i < stream.Count - 1)
                    yield return new WaitForSeconds(interval);
            }

            _runningEntries--;
        }

        IEnumerator RunEntry(SpawnEntry entry, float enemyHpMultiplier, float statMultiplier)
        {
            if (entry == null || entry.Monster == null)
            {
                GameLog.Warn("Wave", "스폰 항목에 몬스터 데이터가 없음");
                yield break;
            }

            _runningEntries++;

            if (entry.StartDelay > 0f)
                yield return new WaitForSeconds(entry.StartDelay);

            for (int i = 0; i < entry.Count; i++)
            {
                Spawn(entry.Monster, enemyHpMultiplier, statMultiplier);

                if (i < entry.Count - 1)
                    yield return new WaitForSeconds(entry.Interval);
            }

            _runningEntries--;
        }

        void Spawn(MonsterData data, float enemyHpMultiplier, float statMultiplier)
        {
            if (data.Prefab == null)
            {
                GameLog.Warn("Wave", $"{data.name}: 프리팹이 비어 있어 스폰 불가");
                return;
            }

            var go = Instantiate(data.Prefab, _path.GetPoint(0), Quaternion.identity, transform);
            var monster = go.GetComponent<Monster>();

            if (monster == null)
            {
                GameLog.Warn("Wave", $"{data.name}: 프리팹에 Monster 컴포넌트가 없어 추가함");
                monster = go.AddComponent<Monster>();
            }

            monster.Initialize(data, _path, enemyHpMultiplier, statMultiplier,
                _stage.HandleMonsterDied, _stage.HandleMonsterReachedExit);

            // 스폰 지점에 연기를 한 번 터뜨려 갑자기 나타나는 느낌을 줄인다
            Rush.Fx.OneShotFx.Spawn(_spawnFx, _path.GetPoint(0));
        }
    }
}
