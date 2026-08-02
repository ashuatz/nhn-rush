using System.Collections;
using Rush.Combat;
using Rush.Data;
using UnityEngine;

namespace Rush.Stage
{
    /// <summary>
    /// 웨이브 데이터를 받아 몬스터를 순차 스폰한다.
    /// StageController가 Initialize로 참조를 주입한다 (인스펙터 순환 참조 회피).
    /// </summary>
    public class WaveSpawner : MonoBehaviour
    {
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

            if (wave == null || wave.Entries == null || wave.Entries.Length == 0)
            {
                GameLog.Warn("Wave", "빈 웨이브 데이터 - 스폰 생략");
                return;
            }

            foreach (var entry in wave.Entries)
                StartCoroutine(RunEntry(entry, enemyHpMultiplier));
        }

        public void StopAll()
        {
            StopAllCoroutines();
            _runningEntries = 0;
        }

        IEnumerator RunEntry(SpawnEntry entry, float enemyHpMultiplier)
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
                Spawn(entry.Monster, enemyHpMultiplier);

                if (i < entry.Count - 1)
                    yield return new WaitForSeconds(entry.Interval);
            }

            _runningEntries--;
        }

        void Spawn(MonsterData data, float enemyHpMultiplier)
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

            monster.Initialize(data, _path, enemyHpMultiplier,
                _stage.HandleMonsterDied, _stage.HandleMonsterReachedExit);
        }
    }
}
