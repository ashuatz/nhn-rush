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

        /// <summary>애니메이터를 붙일 프리팹. RushUnitArtSetup이 리깅 모델을 넣은 것들이다.</summary>
        static readonly string[] TargetPrefabs =
        {
            "Monster_Militia",
            "Monster_HeavyInfantry",
            "Monster_Scout",
            "Monster_EnemyMage",
            "Monster_Centurion",
            "Soldier",
        };

        [MenuItem("Rush/캐릭터 애니메이션 셋업", false, 320)]
        public static void Run()
        {
            ConvertRigsToHumanoid();

            var controller = BuildController();

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
                .Where(p => p.Contains("_rig.fbx") || p.Replace('\\', '/').StartsWith(AnimDir))
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

        static AnimatorController BuildController()
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

            // 매번 새로 만든다. 손으로 고친 게 있으면 덮이므로 임시 셋업으로만 쓸 것.
            AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attacking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;

            var runState = machine.AddState("Run");
            runState.motion = run;

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

                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;

                    if (animator.GetComponent<UnitAnimator>() == null)
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
