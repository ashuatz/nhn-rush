using System;
using Rush.Combat;
using Rush.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rush.Stage
{
    public enum StagePhase
    {
        Ready = 0,
        Running = 1,
        Victory = 2,
        Defeat = 3,
    }

    /// <summary>
    /// 스테이지의 중심 컨트롤러. 골드/생명 보유, 웨이브 진행 상태머신, 조기소환, 배속 제어.
    /// 전투 개체의 처치/도달 보고를 받는 유일한 이벤트 허브이며, UI는 Changed 이벤트를 구독만 한다.
    /// 씬에 미리 배치한다 (런타임 부트스트랩 없음).
    /// </summary>
    public class StageController : MonoBehaviour
    {
        /// <summary>배속 단계. 재시작해도 선택한 배속을 유지한다.</summary>
        public static readonly float[] SpeedSteps = { 1f, 2f, 4f };

        static int _speedIndex;

        [SerializeField] StageData _stageData;
        [SerializeField] DifficultyPreset _difficulty;
        [SerializeField] PathRoute[] _paths;
        [SerializeField] WaveSpawner _spawner;
        [SerializeField] RewardSystem _rewards;

        bool _setupValid;
        int _waveIndex = -1;
        float _nextWaveTimer;

        public StageData Data => _stageData;

        /// <summary>스테이지의 몬스터 루트 4개 (A1/A2/B1/B2). 배열 순서가 스폰 분배 순서다.</summary>
        public PathRoute[] Paths => _paths;

        /// <summary>
        /// origin에서 가장 가까운 루트. 병사 집결지처럼 "어느 길을 막을지"를 정할 때 쓴다.
        /// 유효한 루트가 하나도 없으면 null.
        /// </summary>
        public PathRoute NearestPath(Vector3 origin)
        {
            if (_paths == null)
                return null;

            PathRoute best = null;
            float bestSqr = float.MaxValue;

            foreach (var path in _paths)
            {
                if (path == null || path.PointCount < 2)
                    continue;

                path.ClosestPoint(origin, out float sqrDistance);

                if (sqrDistance >= bestSqr)
                    continue;

                best = path;
                bestSqr = sqrDistance;
            }

            return best;
        }

        public StagePhase Phase { get; private set; }
        public int Gold { get; private set; }
        public int Life { get; private set; }

        /// <summary>마지막으로 시작된 웨이브 번호 (1-base). 시작 전이면 0.</summary>
        public int WaveNumber => _waveIndex + 1;

        public int TotalWaves
        {
            get
            {
                if (_stageData == null)
                    return 0;

                return _stageData.Waves.Length;
            }
        }

        public bool AllWavesStarted => _waveIndex >= TotalWaves - 1;

        public float NextWaveIn => Mathf.Max(0f, _nextWaveTimer);

        /// <summary>1-base 웨이브 번호로 웨이브 데이터를 얻는다. 범위 밖이면 null.</summary>
        public WaveData GetWave(int waveNumber)
        {
            if (_stageData == null || _stageData.Waves == null)
                return null;

            int index = waveNumber - 1;

            if (index < 0 || index >= _stageData.Waves.Length)
                return null;

            return _stageData.Waves[index];
        }

        /// <summary>해당 웨이브가 중간 보스 웨이브인지 (6/12/18).</summary>
        public bool IsBossWave(int waveNumber)
        {
            var wave = GetWave(waveNumber);

            return wave != null && wave.IsBossWave;
        }

        /// <summary>진행 중인 스테이지인지. 건설/조기소환 등 플레이 입력의 공통 조건.</summary>
        public bool IsPlayable
        {
            get
            {
                if (!_setupValid)
                    return false;

                if (Phase == StagePhase.Ready)
                    return true;

                if (Phase == StagePhase.Running)
                    return true;

                return false;
            }
        }

        public float CurrentSpeed => SpeedSteps[_speedIndex];

        public float EnemyHpMultiplier
        {
            get
            {
                if (_difficulty == null)
                    return 1f;

                return _difficulty.EnemyHpMultiplier;
            }
        }

        public float SoldierHpMultiplier
        {
            get
            {
                if (_difficulty == null)
                    return 1f;

                return _difficulty.SoldierHpMultiplier;
            }
        }

        public string DifficultyName
        {
            get
            {
                if (_difficulty == null)
                    return "노멀";

                return _difficulty.DisplayName;
            }
        }

        /// <summary>골드/생명/웨이브/페이즈 등 어떤 상태든 바뀌면 발화. UI는 이것만 구독한다.</summary>
        public event Action Changed;

        void Awake()
        {
            // 씬 재시작 시 정적 상태 잔재 정리
            MonsterRegistry.Clear();
            Soldier.ClearRegistry();
            GameLog.Clear();

            ApplySpeed();
        }

        void Start()
        {
            _setupValid = ValidateSetup();

            if (!_setupValid)
            {
                GameLog.Warn("Stage", "필수 참조가 비어 스테이지를 시작하지 않음 - Stage Command Center에서 씬 셋업 실행 필요");
                Notify();
                return;
            }

            Gold = _stageData.StartGold;
            Life = _stageData.StartLife;
            Phase = StagePhase.Ready;
            _nextWaveTimer = _stageData.FirstWaveDelay;

            GameLog.Info("Stage", $"스테이지 시작 - 난이도 {DifficultyName}, 골드 {Gold}, 생명 {Life}");

            Notify();
        }

        /// <summary>필수 참조를 전부 확인한다. 하나라도 빠지면 진행 자체를 막는다.</summary>
        bool ValidateSetup()
        {
            bool valid = true;

            if (_stageData == null)
            {
                GameLog.Warn("Stage", "StageData 참조가 비어 있음");
                valid = false;
            }
            else if (_stageData.Waves == null || _stageData.Waves.Length == 0)
            {
                GameLog.Warn("Stage", "StageData에 웨이브가 없음");
                valid = false;
            }

            if (_paths == null || _paths.Length == 0)
            {
                GameLog.Warn("Stage", "PathRoute 참조가 비어 있음");
                valid = false;
            }

            if (_spawner == null)
            {
                GameLog.Warn("Stage", "WaveSpawner 참조가 비어 있음");
                valid = false;
            }

            if (!valid)
                return false;

            // 루트별 유효성은 스포너가 판정한다 (웨이포인트 2개 미만인 루트는 분배에서 빠진다)
            return _spawner.Initialize(this, _paths);
        }

        void Update()
        {
            if (!IsPlayable)
                return;

            // 다음 웨이브 자동 진행 카운트다운
            if (!AllWavesStarted)
            {
                _nextWaveTimer -= Time.deltaTime;

                if (_nextWaveTimer <= 0f)
                    StartNextWave();
            }

            CheckVictory();
        }

        void CheckVictory()
        {
            if (Phase != StagePhase.Running)
                return;

            if (!AllWavesStarted)
                return;

            if (_spawner.IsSpawning)
                return;

            if (MonsterRegistry.Active.Count > 0)
                return;

            Phase = StagePhase.Victory;
            GameLog.Info("Stage", $"승리 - {TotalWaves}웨이브 방어 완료");

            Notify();
        }

        void StartNextWave()
        {
            if (AllWavesStarted)
                return;

            // 보상 시스템이 웨이브 시작을 가로챌 수 있다 (디밍 -> 카드 선택 -> 재개)
            if (_rewards != null && _rewards.TryInterceptWaveStart(WaveNumber + 1, StartWaveNow))
            {
                _nextWaveTimer = _stageData.WaveInterval;
                return;
            }

            StartWaveNow();
        }

        void StartWaveNow()
        {
            if (AllWavesStarted)
                return;

            // 보상 대기 중 패배/승리가 확정됐다면 뒤늦게 도착한 재개 콜백을 무시한다
            if (Phase == StagePhase.Victory || Phase == StagePhase.Defeat)
                return;

            _waveIndex++;
            _nextWaveTimer = _stageData.WaveInterval;
            Phase = StagePhase.Running;

            var wave = _stageData.Waves[_waveIndex];
            _spawner.StartWave(wave, EnemyHpMultiplier);

            GameLog.Info("Wave", $"웨이브 {WaveNumber}/{TotalWaves} 시작");

            // 보상: 웨이브 시작 수입 (이자/채권)
            int rewardGold = RewardSystem.WaveStartGold(Gold);

            if (rewardGold > 0)
            {
                AddGold(rewardGold);
                GameLog.Info("Reward", $"웨이브 수입 +{rewardGold}G");
            }

            Notify();
        }

        /// <summary>지금 조기소환하면 받을 보너스 골드 = 다음 웨이브 예산의 15% (5 단위 반올림).</summary>
        public int EarlyCallBonus
        {
            get
            {
                if (!CanCallEarly)
                    return 0;

                var next = GetWave(WaveNumber + 1);

                if (next == null)
                    return 0;

                float raw = next.Budget * _stageData.EarlyCallBudgetFraction;

                return Mathf.RoundToInt(raw / 5f) * 5;
            }
        }

        public bool CanCallEarly
        {
            get
            {
                if (!IsPlayable)
                    return false;

                // 보상 선택 중에는 웨이브를 앞당길 수 없다
                if (_rewards != null && _rewards.OfferActive)
                    return false;

                return !AllWavesStarted;
            }
        }

        public void CallNextWaveEarly()
        {
            if (!CanCallEarly)
                return;

            int bonus = EarlyCallBonus;

            AddGold(bonus);
            GameLog.Info("Wave", $"조기소환 - 보너스 골드 +{bonus}");

            StartNextWave();
        }

        public bool TrySpend(int cost)
        {
            if (Gold < cost)
                return false;

            Gold -= cost;
            Notify();

            return true;
        }

        public void AddGold(int amount)
        {
            if (amount <= 0)
                return;

            Gold += amount;
            Notify();
        }

        public void HandleMonsterDied(Monster monster)
        {
            // 보상: 막타 귀속/상태 기반 처치 보너스. 킬 보상은 웨이브 배수 반영값.
            int baseGold = monster.GoldReward;
            int bonus = RewardSystem.KillGoldBonus(monster);

            // 현상금 수거: 용병(병사)이 막타를 치면 골드 배수 (올림, 전장 회수와 합연산)
            bonus += BountyBonus(monster, baseGold);

            AddGold(baseGold + bonus);

            if (bonus > 0)
                GameLog.Info("Kill", $"{monster.Data.DisplayName} 처치 (+{baseGold}G, 보너스 +{bonus}G)");
            else
                GameLog.Info("Kill", $"{monster.Data.DisplayName} 처치 (+{baseGold}G)");

            // 중간 보스 처치 보상 (한 판 3회)
            if (monster.Data.IsBoss && _rewards != null && Phase == StagePhase.Running)
                _rewards.TryOfferBossReward();

            Notify();
        }

        /// <summary>현상금 수거(분기 스킬): 병사 막타 골드 배수. 올림 처리.</summary>
        static int BountyBonus(Monster monster, int baseGold)
        {
            var source = monster.LastHitSource;

            if (!source.FromSoldier || source.Tower == null)
                return 0;

            if (!source.Tower.TryGetSkill(BranchSkillType.BountyCollect, out var bounty, out int level))
                return 0;

            float multiplier = bounty.ValueAt(level);

            if (multiplier <= 1f)
                return 0;

            return Mathf.CeilToInt(baseGold * (multiplier - 1f));
        }

        public void HandleMonsterReachedExit(Monster monster)
        {
            if (!IsPlayable)
                return;

            Life -= monster.Data.LifeDamage;
            GameLog.Info("Leak", $"{monster.Data.DisplayName} 출구 도달 (생명 -{monster.Data.LifeDamage})");

            if (Life <= 0)
            {
                Life = 0;
                Phase = StagePhase.Defeat;
                _spawner.StopAll();

                // 열려 있던 보상 제시는 무효 (선택해도 웨이브가 시작되면 안 된다)
                if (_rewards != null)
                    _rewards.CancelOffer();

                GameLog.Info("Stage", "패배 - 생명 소진");
            }

            Notify();
        }

        // ---------- 배속 ----------

        public void CycleSpeed()
        {
            _speedIndex = (_speedIndex + 1) % SpeedSteps.Length;

            ApplySpeed();
            GameLog.Info("Stage", $"배속 {CurrentSpeed:0.#}x");

            Notify();
        }

        /// <summary>획득 보상 바 등 UI가 게임을 잠시 멈출 때 사용. 보상 디밍과 별개로 유지된다.</summary>
        public bool UiPauseActive { get; private set; }

        public void SetUiPause(bool paused)
        {
            UiPauseActive = paused;

            ApplySpeed();
        }

        /// <summary>
        /// 일시정지 메뉴가 열려 있는 동안 true.
        /// UiPauseActive와 따로 두는 이유: 보상 사이드바가 같은 플래그를 쓰고 있어,
        /// 하나로 합치면 메뉴를 닫을 때 사이드바가 열어둔 정지까지 같이 풀린다.
        /// </summary>
        public bool MenuPauseActive { get; private set; }

        public void SetMenuPause(bool paused)
        {
            MenuPauseActive = paused;

            ApplySpeed();
        }

        void ApplySpeed()
        {
            // 보상 선택(디밍)이나 UI 일시정지 중에는 정지를 유지한다. 배속 인덱스만 바뀌고 닫힐 때 반영된다.
            if (_rewards != null && _rewards.OfferActive)
                return;

            if (UiPauseActive || MenuPauseActive)
            {
                Time.timeScale = 0f;
                return;
            }

            Time.timeScale = SpeedSteps[_speedIndex];
        }

        /// <summary>보상 선택 등으로 timeScale을 0으로 만들었던 쪽이 원래 배속으로 되돌릴 때 호출.</summary>
        public void ReapplySpeed()
        {
            ApplySpeed();
        }

        public void RestartStage()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void Notify()
        {
            Changed?.Invoke();
        }
    }
}
