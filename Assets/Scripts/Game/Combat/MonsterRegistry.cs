using System;
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

        static Vector3 _sortCenter;
        static readonly Comparison<Monster> _byDistanceToCenter = CompareByDistanceToCenter;

        static int CompareByDistanceToCenter(Monster a, Monster b)
        {
            float da = (a.transform.position - _sortCenter).sqrMagnitude;
            float db = (b.transform.position - _sortCenter).sqrMagnitude;

            return da.CompareTo(db);
        }

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

                // 루트마다 길이가 다르므로 진행률로 비교한다 (출구에 가장 가까운 적 우선)
                if (monster.PathProgressRatio <= bestProgress)
                    continue;

                best = monster;
                bestProgress = monster.PathProgressRatio;
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

                // 루트마다 길이가 다르므로 진행률로 비교한다
                if (!monster.IsBlocked && monster.PathProgressRatio > bestFreeProgress)
                {
                    bestFree = monster;
                    bestFreeProgress = monster.PathProgressRatio;
                }

                if (monster.PathProgressRatio > bestAnyProgress)
                {
                    bestAny = monster;
                    bestAnyProgress = monster.PathProgressRatio;
                }
            }

            if (bestFree != null)
                return bestFree;

            return bestAny;
        }

        /// <summary>반경 안에서 가까운 순으로 최대 max마리를 고른다 (처치 시 유도 발사용).</summary>
        public static void CollectNearest(Vector3 center, float radius, bool includeFlying,
            int max, Monster exclude, List<Monster> results)
        {
            results.Clear();

            if (max <= 0)
                return;

            float radiusSqr = radius * radius;

            foreach (var monster in _active)
            {
                if (monster == null || !monster.IsAlive)
                    continue;

                if (monster == exclude)
                    continue;

                if (!includeFlying && monster.Data.IsFlying)
                    continue;

                if ((monster.transform.position - center).sqrMagnitude > radiusSqr)
                    continue;

                results.Add(monster);
            }

            // 후보가 발수보다 적어 순환 배정될 때도 가까운 순서를 지켜야 한다
            if (results.Count < 2)
                return;

            _sortCenter = center;
            results.Sort(_byDistanceToCenter);

            if (results.Count <= max)
                return;

            results.RemoveRange(max, results.Count - max);
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
