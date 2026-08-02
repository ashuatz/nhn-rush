using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rush.Stage
{
    /// <summary>
    /// 런타임 이벤트 로그의 단일 창구.
    /// 스폰/처치/도달/건설/피해 등 핵심 이벤트를 링버퍼로 보관하고 DebugDashboard가 구독한다.
    /// </summary>
    public static class GameLog
    {
        public struct Entry
        {
            public float Time;
            public string Category;
            public string Message;
        }

        const int MaxEntries = 200;

        static readonly List<Entry> _entries = new List<Entry>(MaxEntries);

        /// <summary>피해 계산 단위의 상세 로그 on/off (대시보드에서 토글).</summary>
        public static bool VerboseCombat;

        public static event Action<Entry> Logged;

        public static IReadOnlyList<Entry> Entries => _entries;

        public static void Info(string category, string message)
        {
            var entry = new Entry
            {
                Time = Application.isPlaying ? UnityEngine.Time.time : 0f,
                Category = category,
                Message = message,
            };

            if (_entries.Count >= MaxEntries)
                _entries.RemoveAt(0);

            _entries.Add(entry);

            Debug.Log($"[Rush][{category}] {message}");

            Logged?.Invoke(entry);
        }

        public static void Warn(string category, string message)
        {
            var entry = new Entry
            {
                Time = Application.isPlaying ? UnityEngine.Time.time : 0f,
                Category = category,
                Message = message,
            };

            if (_entries.Count >= MaxEntries)
                _entries.RemoveAt(0);

            _entries.Add(entry);

            Debug.LogWarning($"[Rush][{category}] {message}");

            Logged?.Invoke(entry);
        }

        public static void Clear()
        {
            _entries.Clear();
        }
    }
}
