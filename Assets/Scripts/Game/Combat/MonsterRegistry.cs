using System.Collections.Generic;
using UnityEngine;

namespace Rush.Combat
{
    /// <summary>
    /// 활성 몬스터 목록. 타워 타겟 탐색은 물리 캐스트 대신 이 목록 순회로 처리한다.
    /// Monster가 스스로 등록/해제하며, 씬 재시작 시 StageController가 Clear한다.
    /// </summary>
    public static class MonsterRegistry
    {
        static readonly List<Monster> _active = new List<Monster>();

        public static IReadOnlyList<Monster> Active => _active;

        public static void Register(Monster monster)
        {
            if (_active.Contains(monster))
                return;

            _active.Add(monster);
        }

        public static void Unregister(Monster monster)
        {
            _active.Remove(monster);
        }

        public static void Clear()
        {
            _active.Clear();
        }

        /// <summary>사거리 내에서 경로를 가장 멀리 진행한 몬스터를 고른다 (킹덤러쉬 기본 타겟팅).</summary>
        public static Monster FindTarget(Vector3 origin, float range, bool includeFlying)
        {
            Monster best = null;
            float bestProgress = float.MinValue;
            float rangeSqr = range * range;

            foreach (var monster in _active)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                if (!includeFlying && monster.Data.IsFlying)
                    continue;

                float distSqr = (monster.transform.position - origin).sqrMagnitude;

                if (distSqr > rangeSqr)
                    continue;

                if (monster.PathProgress <= bestProgress)
                    continue;

                best = monster;
                bestProgress = monster.PathProgress;
            }

            return best;
        }

        /// <summary>
        /// 병사가 맡을 지상 표적을 고른다. 아직 저지되지 않은 몬스터를 우선하고,
        /// 전부 저지 중이면 가장 앞선 몬스터를 함께 두들기도록 반환한다.
        /// </summary>
        public static Monster FindBlockTarget(Vector3 origin, float range)
        {
            Monster bestFree = null;
            float bestFreeProgress = float.MinValue;

            Monster bestAny = null;
            float bestAnyProgress = float.MinValue;

            float rangeSqr = range * range;

            foreach (var monster in _active)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                if (monster.Data.IsFlying)
                    continue;

                float distSqr = (monster.transform.position - origin).sqrMagnitude;

                if (distSqr > rangeSqr)
                    continue;

                if (!monster.IsBlocked && monster.PathProgress > bestFreeProgress)
                {
                    bestFree = monster;
                    bestFreeProgress = monster.PathProgress;
                }

                if (monster.PathProgress > bestAnyProgress)
                {
                    bestAny = monster;
                    bestAnyProgress = monster.PathProgress;
                }
            }

            if (bestFree != null)
                return bestFree;

            return bestAny;
        }

        /// <summary>반경 내 몬스터를 전부 수집한다 (포병 광역용).</summary>
        public static void CollectInRange(Vector3 center, float radius, bool includeFlying, List<Monster> results)
        {
            results.Clear();

            float radiusSqr = radius * radius;

            foreach (var monster in _active)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                if (!includeFlying && monster.Data.IsFlying)
                    continue;

                float distSqr = (monster.transform.position - center).sqrMagnitude;

                if (distSqr > radiusSqr)
                    continue;

                results.Add(monster);
            }
        }
    }
}
