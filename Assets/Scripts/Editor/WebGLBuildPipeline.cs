using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rush.EditorTools
{
    /// <summary>
    /// CI(GitHub Actions) 배치모드에서 호출하는 WebGL 빌드 진입점.
    /// GitHub Pages는 Content-Encoding 헤더를 붙여 줄 수 없으므로 압축 폴백을 강제로 켠다.
    /// 로컬 빌드 설정을 건드리지 않도록 CI에서만 이 메서드를 쓴다.
    /// </summary>
    public static class WebGLBuildPipeline
    {
        private const string DefaultBuildPath = "build/WebGL/WebGL";
        private const string BuildPathArg = "-customBuildPath";
        private const string BuildVersionArg = "-buildVersion";

        /// <summary>GitHub Pages 배포용 WebGL 빌드. -executeMethod 대상.</summary>
        public static void BuildForPages()
        {
            string[] scenes = CollectEnabledScenes();

            if (scenes.Length == 0)
            {
                Fail("EditorBuildSettings에 활성화된 씬이 없다. 빌드할 대상이 없음.");
                return;
            }

            string buildPath = ReadArgument(BuildPathArg, DefaultBuildPath);
            string version = ReadArgument(BuildVersionArg, null);

            if (!string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
            }

            ApplyPagesFriendlySettings();

            Directory.CreateDirectory(buildPath);

            Debug.Log($"[WebGLBuildPipeline] 빌드 시작. path={buildPath}, version={PlayerSettings.bundleVersion}, scenes={scenes.Length}");
            foreach (string scene in scenes)
            {
                Debug.Log($"[WebGLBuildPipeline] scene: {scene}");
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = buildPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[WebGLBuildPipeline] 결과={summary.result}, 시간={summary.totalTime}, 크기={summary.totalSize / (1024 * 1024)}MB, 에러={summary.totalErrors}");

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"WebGL 빌드 실패: {summary.result}");
                return;
            }

            EditorApplication.Exit(0);
        }

        /// <summary>Pages 정적 호스팅에서 그대로 동작하도록 WebGL 설정을 맞춘다.</summary>
        private static void ApplyPagesFriendlySettings()
        {
            // Pages는 .gz에 Content-Encoding을 못 붙인다. 폴백이 꺼져 있으면 로더가 파일을 해석하지 못하고 멈춘다.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;

            // 나머지 WebGL 설정(예외 처리 수준, 데이터 캐싱, 메모리 등)은 프로젝트 설정을 그대로 따른다.
        }

        private static string[] CollectEnabledScenes()
        {
            List<string> scenes = new List<string>();

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }

                scenes.Add(scene.path);
            }

            return scenes.ToArray();
        }

        private static string ReadArgument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[WebGLBuildPipeline] {message}");
            EditorApplication.Exit(1);
        }
    }
}
