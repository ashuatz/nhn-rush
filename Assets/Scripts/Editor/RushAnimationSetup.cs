using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rush.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Rush.EditorTools
{
    /// <summary>
    /// 캐릭터 임시 애니메이션 셋업. Hero/soldier/Anim의 sword 클립을 모든 유닛에 붙인다.
    ///
    /// 리그는 전부 Mixamo 스켈레톤이지만 루트 노드 이름이 다르다
    /// (soldier_rig=Mixamo, Anemy=Armature). Generic은 경로로 바인딩해서 이대로는 안 붙으므로
    /// 리그와 클립을 전부 Humanoid로 바꿔 아바타 재타게팅으로 돌린다.
    ///
    /// 멱등이다. 여러 번 실행해도 같은 결과가 된다.
    /// 실행 순서: 머티리얼 연결 -> 리깅 모델 연결 -> 이 툴.
    /// </summary>
    public static class RushAnimationSetup
    {
        const string CharacterRoot = "Assets/fbx/character";
        const string AnimDir = "Assets/fbx/character/Hero/soldier/Anim";
        const string PrefabDir = "Assets/RushGame/Prefabs";
        const string ControllerDir = "Assets/RushGame/Animation";
        const string ControllerPath = ControllerDir + "/UnitSword.controller";

        // 상태별 클립. 임시 배정이라 여기만 바꾸면 교체된다.
        const string IdleClip = "SwordWait";
        const string RunClip = "SwordRun";
        const string AttackClip = "SwordAttack01";
        const string DieClip = "SwordDie01";

        /// <summary>반복 재생할 클립. 공격/죽음은 한 번만 재생한다.</summary>
        static readonly string[] LoopingClips = { IdleClip, RunClip, AttackClip };

        /// <summary>
        /// Run 상태의 재생 속도 배율. UnitAnimator가 실측 이동 속도로 채운다.
        /// 느린 유닛(보스 0.8)은 느리게, 빠른 유닛(정찰병 3.2)은 빠르게 발을 젓는다.
        /// </summary>
        const string MoveSpeedParam = "MoveSpeed";

        /// <summary>애니메이터를 붙일 프리팹. RushUnitArtSetup이 리깅 모델을 넣은 것들이다.</summary>
        static readonly string[] TargetPrefabs =
        {
            "Monster_Militia",
            "Monster_HeavyInfantry",
            "Monster_Scout",
            "Monster_EnemyMage",
            "Monster_Centurion",
            "Soldier",
            "Monster_MidBoss1",
            "Monster_MidBoss2",
            "Monster_MidBoss3",
            "Monster_FinalBoss",
        };

        [MenuItem("Rush/캐릭터 애니메이션 셋업", false, 320)]
        public static void Run()
        {
            Setup(forceRebuild: false);
        }

        /// <summary>
        /// 컨트롤러 그래프를 손으로 고쳐 놨다가 표준 상태로 되돌릴 때만 쓴다.
        /// 에셋 자체는 그대로 두고 내용만 다시 만들므로 프리팹 참조는 끊기지 않는다.
        /// </summary>
        [MenuItem("Rush/캐릭터 애니메이션 셋업 (컨트롤러 강제 재생성)", false, 321)]
        public static void RunForced()
        {
            Setup(forceRebuild: true);
        }

        static void Setup(bool forceRebuild)
        {
            // 리깅 모델이 없으면 붙일 Animator 자체가 없다. 순서를 틀릴 여지가 없도록 여기서 같이 돌린다
            // (이미 연결된 프리팹은 RushUnitArtSetup이 건너뛰므로 여러 번 눌러도 안전하다).
            RushUnitArtSetup.Run();

            ConvertRigsToHumanoid();

            var controller = BuildController(forceRebuild);

            if (controller == null)
                return;

            AttachToPrefabs(controller);

            AssetDatabase.SaveAssets();

            Debug.Log("[Anim] 셋업 완료. 프리팹을 열어 모션이 도는지 확인할 것.");
        }

        // ── 1. Humanoid 전환 ─────────────────────────────────────────────────

        /// <summary>
        /// 캐릭터 리그와 애니메이션 FBX를 전부 Humanoid로 바꾼다.
        /// Mixamo 본 이름이라 아바타 자동 매핑이 깔끔하게 잡힌다.
        /// </summary>
        static void ConvertRigsToHumanoid()
        {
            var paths = AssetDatabase.FindAssets("t:Model", new[] { CharacterRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                // 적 리그는 "_Rig.fbx", 병사는 "_rig.fbx"로 파일명 대소문자가 섞여 있다
                .Where(p => p.EndsWith("_rig.fbx", System.StringComparison.OrdinalIgnoreCase)
                            || p.Replace('\\', '/').StartsWith(AnimDir))
                .ToList();

            int converted = 0;

            foreach (var path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;

                if (importer == null)
                    continue;

                bool dirty = false;

                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    dirty = true;
                }

                if (ApplyLoopSettings(importer, path))
                    dirty = true;

                if (!dirty)
                    continue;

                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                converted++;
            }

            Debug.Log($"[Anim] Humanoid 전환 {converted}건");
        }

        /// <summary>Idle/Run/Attack은 반복, 죽음은 한 번만. 바뀐 게 있으면 true.</summary>
        static bool ApplyLoopSettings(ModelImporter importer, string path)
        {
            if (!path.Replace('\\', '/').StartsWith(AnimDir))
                return false;

            var clips = importer.clipAnimations;

            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;

            if (clips == null || clips.Length == 0)
                return false;

            bool loop = LoopingClips.Contains(Path.GetFileNameWithoutExtension(path));
            bool dirty = false;

            foreach (var clip in clips)
            {
                if (clip.loopTime == loop)
                    continue;

                clip.loopTime = loop;
                dirty = true;
            }

            if (dirty)
                importer.clipAnimations = clips;

            return dirty;
        }

        // ── 2. 애니메이터 컨트롤러 ───────────────────────────────────────────

        /// <summary>
        /// 컨트롤러를 만들거나 갱신한다.
        ///
        /// 에셋을 절대 지우지 않는 것이 핵심이다. DeleteAsset으로 지우고 다시 만들면
        /// 프리팹이 물고 있던 컨트롤러 오브젝트가 그 자리에서 파괴돼 Animator의
        /// Controller 칸이 Missing으로 뜨고, 재임포트나 에디터 재시작 전까지 모션이 죽는다.
        /// 파일 전체가 새 fileID로 다시 쓰이는 바람에 매 실행마다 통짜 diff가 나는 문제도 같이 없앤다.
        ///
        /// 이미 같은 내용이면 손대지 않는다. 그래프가 다르거나 forceRebuild면 에셋은 유지한 채 내용만 갈아친다.
        /// </summary>
        static AnimatorController BuildController(bool forceRebuild)
        {
            var idle = LoadClip(IdleClip);
            var run = LoadClip(RunClip);
            var attack = LoadClip(AttackClip);
            var die = LoadClip(DieClip);

            if (idle == null || run == null || attack == null || die == null)
            {
                Debug.LogError("[Anim] 클립을 찾지 못했다. Anim 폴더의 FBX를 확인할 것.");
                return null;
            }

            EnsureFolder(ControllerDir);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else if (!forceRebuild && Matches(controller, idle, run, attack, die))
            {
                Debug.Log($"[Anim] 컨트롤러 유지: {ControllerPath}");

                return controller;
            }
            else
            {
                ClearGraph(controller);
            }

            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attacking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            // 기본값 1 = 기준 속도로 달릴 때 클립 원래 속도
            var moveSpeed = new AnimatorControllerParameter
            {
                name = MoveSpeedParam,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            };

            controller.AddParameter(moveSpeed);

            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;

            var runState = machine.AddState("Run");
            runState.motion = run;

            // 달리기만 속도를 태운다. 공격/죽음은 연출 타이밍이라 원래 속도를 유지한다.
            runState.speedParameterActive = true;
            runState.speedParameter = MoveSpeedParam;

            var attackState = machine.AddState("Attack");
            attackState.motion = attack;

            var dieState = machine.AddState("Die");
            dieState.motion = die;

            machine.defaultState = idleState;

            // 공격이 이동보다 우선한다 (저지당한 몬스터는 제자리에서 때린다)
            Link(idleState, attackState, ("Attacking", true));
            Link(runState, attackState, ("Attacking", true));
            Link(attackState, runState, ("Attacking", false), ("Moving", true));
            Link(attackState, idleState, ("Attacking", false), ("Moving", false));

            Link(idleState, runState, ("Moving", true), ("Attacking", false));
            Link(runState, idleState, ("Moving", false), ("Attacking", false));

            // 죽음은 어느 상태에서든 즉시. 다시 나가지 않는다.
            var toDie = machine.AddAnyStateTransition(dieState);
            toDie.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            toDie.hasExitTime = false;
            toDie.duration = 0.05f;
            toDie.canTransitionToSelf = false;

            EditorUtility.SetDirty(controller);

            Debug.Log($"[Anim] 컨트롤러 생성: {ControllerPath}");

            return controller;
        }

        /// <summary>
        /// 이미 원하는 그래프인지. 상태 이름과 물린 클립, 파라미터만 본다
        /// (전이 세부값은 여기서 검사하지 않는다 - 손으로 튜닝한 값을 헛되게 날릴 이유가 없다).
        /// 같으면 에셋을 건드리지 않아 재실행해도 diff가 나지 않는다.
        /// </summary>
        static bool Matches(AnimatorController controller, AnimationClip idle, AnimationClip run,
                            AnimationClip attack, AnimationClip die)
        {
            if (controller.layers.Length == 0)
                return false;

            var parameterNames = controller.parameters.Select(p => p.name).ToList();

            if (parameterNames.Count != 4)
                return false;

            if (!parameterNames.Contains("Moving") || !parameterNames.Contains("Attacking")
                || !parameterNames.Contains("Dead") || !parameterNames.Contains(MoveSpeedParam))
                return false;

            var states = controller.layers[0].stateMachine.states;

            if (states.Length != 4)
                return false;

            if (!HasState(states, "Idle", idle)
                || !HasState(states, "Run", run)
                || !HasState(states, "Attack", attack)
                || !HasState(states, "Die", die))
                return false;

            // 파라미터만 있고 Run에 안 물려 있으면 배율이 먹지 않는다
            var runState = FindState(states, "Run");

            return runState != null && runState.speedParameterActive && runState.speedParameter == MoveSpeedParam;
        }

        static bool HasState(ChildAnimatorState[] states, string name, AnimationClip clip)
        {
            var state = FindState(states, name);

            return state != null && state.motion == clip;
        }

        static AnimatorState FindState(ChildAnimatorState[] states, string name)
        {
            foreach (var child in states)
            {
                if (child.state != null && child.state.name == name)
                    return child.state;
            }

            return null;
        }

        /// <summary>
        /// 컨트롤러 에셋은 남기고 그래프만 비운다. 지우고 다시 만들면 참조가 끊기므로 이 경로만 쓴다.
        /// 상태를 지우면 그 상태에 붙은 전이도 같이 사라진다.
        /// </summary>
        static void ClearGraph(AnimatorController controller)
        {
            foreach (var parameter in controller.parameters)
                controller.RemoveParameter(parameter);

            // 레이어가 없는 컨트롤러는 아래에서 layers[0]을 못 쓴다 (손으로 지웠을 때만 나오는 경우)
            if (controller.layers.Length == 0)
                controller.AddLayer("Base Layer");

            var machine = controller.layers[0].stateMachine;

            foreach (var transition in machine.anyStateTransitions)
                machine.RemoveAnyStateTransition(transition);

            foreach (var transition in machine.entryTransitions)
                machine.RemoveEntryTransition(transition);

            foreach (var child in machine.stateMachines)
                machine.RemoveStateMachine(child.stateMachine);

            foreach (var child in machine.states)
                machine.RemoveState(child.state);
        }

        static void Link(AnimatorState from, AnimatorState to, params (string Name, bool Value)[] conditions)
        {
            var transition = from.AddTransition(to);

            transition.hasExitTime = false;
            transition.duration = 0.1f;

            foreach (var condition in conditions)
            {
                var mode = condition.Value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                transition.AddCondition(mode, 0f, condition.Name);
            }
        }

        static AnimationClip LoadClip(string clipFileName)
        {
            string path = $"{AnimDir}/{clipFileName}.fbx";

            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        // ── 3. 프리팹 배선 ───────────────────────────────────────────────────

        static void AttachToPrefabs(AnimatorController controller)
        {
            int wired = 0;

            foreach (var name in TargetPrefabs)
            {
                string path = $"{PrefabDir}/{name}.prefab";

                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogWarning($"[Anim] 프리팹 없음: {path}");
                    continue;
                }

                var contents = PrefabUtility.LoadPrefabContents(path);

                try
                {
                    var animator = contents.GetComponentInChildren<Animator>(true);

                    if (animator == null)
                    {
                        Debug.LogWarning($"[Anim] {name}: Animator가 없다. " +
                                         "'Rush/유닛 프리팹에 리깅 모델 연결'을 먼저 실행할 것.");
                        continue;
                    }

                    bool needsUnitAnimator = animator.GetComponent<UnitAnimator>() == null;
                    bool alreadyWired = animator.runtimeAnimatorController == controller
                                        && !animator.applyRootMotion
                                        && !needsUnitAnimator;

                    // 이미 같으면 저장하지 않는다. 매번 저장하면 바뀐 것 없이 프리팹 diff만 쌓인다.
                    if (alreadyWired)
                        continue;

                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;

                    if (needsUnitAnimator)
                        animator.gameObject.AddComponent<UnitAnimator>();

                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    wired++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            Debug.Log($"[Anim] 프리팹 배선 {wired}건");
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
