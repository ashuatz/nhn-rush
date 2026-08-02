using UnityEngine;

namespace Rush.Data
{
    /// <summary>난이도 배율. 기획서(코어 룰) 5장.</summary>
    [CreateAssetMenu(menuName = "Rush/Difficulty Preset", fileName = "DifficultyPreset")]
    public class DifficultyPreset : ScriptableObject
    {
        public string DisplayName = "노멀";
        public float EnemyHpMultiplier = 1f;
        public float SoldierHpMultiplier = 1f;
    }
}
