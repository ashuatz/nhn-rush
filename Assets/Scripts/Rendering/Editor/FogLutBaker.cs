using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Common.Rendering.EditorTools
{
    /// <summary>
    /// 그라디언트 한 쌍을 2D 포그 LUT 텍스처로 굽는다.
    ///
    /// 픽셀(x, y) = 거리그라디언트(x축) * 높이그라디언트(y축) (알파 포함 성분별 곱).
    /// 높이 그라디언트를 전부 흰색/불투명으로 두면 순수 거리 포그가 된다.
    /// </summary>
    static class FogLutBaker
    {
        /// <summary>LUT 해상도. U축은 거리 계조라 넉넉히, V축은 높이라 절반 이하로 충분하다.</summary>
        public const int LutWidth = 256;
        public const int LutHeight = 64;

        /// <summary>압축 오버라이드를 걷어낼 빌드 타겟들.</summary>
        static readonly string[] OverridablePlatforms = { "Standalone", "Android", "iPhone", "WebGL" };

        /// <summary>
        /// 그라디언트 쌍을 픽셀 배열로 평가한다. 반환 배열은 아래에서 위로(y=0이 V=0) 채워진다.
        /// </summary>
        public static Color32[] EvaluatePixels(FogLutGradientData data, int width, int height)
        {
            Color32[] pixels = new Color32[width * height];

            // 마지막 픽셀이 정확히 t=1이 되도록 (width-1)로 나눈다.
            // 그래야 LUT 좌표 1이 그라디언트의 끝 키와 일치한다.
            float widthDivisor = Mathf.Max(1, width - 1);
            float heightDivisor = Mathf.Max(1, height - 1);

            for (int y = 0; y < height; y++)
            {
                Color heightColor = data.heightGradient.Evaluate(y / heightDivisor);
                int rowOffset = y * width;

                for (int x = 0; x < width; x++)
                {
                    Color distanceColor = data.distanceGradient.Evaluate(x / widthDivisor);
                    pixels[rowOffset + x] = distanceColor * heightColor;
                }
            }

            return pixels;
        }

        /// <summary>
        /// 창 안에서만 쓰는 임시 프리뷰 텍스처를 채운다. 에셋으로 저장하지 않는다.
        /// </summary>
        public static void FillPreview(Texture2D preview, FogLutGradientData data)
        {
            if (preview == null)
                return;

            preview.SetPixels32(EvaluatePixels(data, preview.width, preview.height));
            preview.Apply(false);
        }

        /// <summary>
        /// 베이커가 덮어써도 되는 경로인지 확인한다.
        /// PNG로만 인코딩하므로 다른 확장자에 쓰면 에셋이 깨진다.
        /// </summary>
        public static bool IsBakeablePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            return assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// LUT을 PNG로 저장하고 임포터를 세팅한 뒤, 다시 편집할 수 있도록
        /// 그라디언트를 userData에 실어 둔다.
        /// </summary>
        /// <returns>임포트된 LUT 텍스처. 실패하면 null.</returns>
        public static Texture2D Bake(FogLutGradientData data, string assetPath)
        {
            if (data == null)
                return null;

            // PNG 바이트를 .tga/.jpg 경로에 쓰면 해당 에셋을 손상시킨다.
            if (!IsBakeablePath(assetPath))
            {
                Debug.LogError($"[{nameof(FogLutBaker)}] PNG 경로만 구울 수 있다: {assetPath}");
                return null;
            }

            Texture2D staging = new Texture2D(LutWidth, LutHeight, TextureFormat.RGBA32, false, false);

            try
            {
                staging.SetPixels32(EvaluatePixels(data, LutWidth, LutHeight));
                staging.Apply(false);

                File.WriteAllBytes(ToAbsolutePath(assetPath), staging.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(staging);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            ApplyImportSettings(assetPath, data);

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// <summary>
        /// 굽는 데 쓴 그라디언트를 텍스처에서 되읽는다.
        /// </summary>
        /// <returns>userData에 그라디언트가 있으면 true.</returns>
        public static bool TryLoadGradients(Texture2D lut, FogLutGradientData into)
        {
            if (lut == null)
                return false;

            if (into == null)
                return false;

            string assetPath = AssetDatabase.GetAssetPath(lut);

            if (string.IsNullOrEmpty(assetPath))
                return false;

            AssetImporter importer = AssetImporter.GetAtPath(assetPath);

            if (importer == null)
                return false;

            if (string.IsNullOrEmpty(importer.userData))
                return false;

            if (!importer.userData.Contains(nameof(FogLutGradientData.distanceGradient)))
                return false;

            EditorJsonUtility.FromJsonOverwrite(importer.userData, into);

            return true;
        }

        static void ApplyImportSettings(string assetPath, FogLutGradientData data)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;

            // 그라디언트는 sRGB 표시 공간에서 편집되므로 sRGB로 임포트해야
            // 셰이더에서 선형 변환된 색이 편집 화면과 일치한다.
            importer.sRGBTexture = true;

            importer.alphaSource = TextureImporterAlphaSource.FromInput;

            // 알파를 투명도로 처리하면 Unity가 투명 영역의 RGB를 확장/보정해
            // LUT의 포그 색이 뭉개진다. LUT의 알파는 농도값이므로 반드시 끈다.
            importer.alphaIsTransparency = false;

            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.maxTextureSize = Mathf.Max(LutWidth, LutHeight);

            // 256x64 RGBA32는 64KB라 압축 이득이 없고, 블록 압축은 계조에 밴딩을 만든다.
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            // 기본 설정만 바꾸면 플랫폼별 오버라이드가 남아 빌드에서 압축될 수 있다.
            ClearPlatformOverrides(importer);

            importer.userData = EditorJsonUtility.ToJson(data, false);

            importer.SaveAndReimport();
        }

        static void ClearPlatformOverrides(TextureImporter importer)
        {
            foreach (string platform in OverridablePlatforms)
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);

                if (!settings.overridden)
                    continue;

                settings.overridden = false;
                importer.SetPlatformTextureSettings(settings);
            }
        }

        static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            if (string.IsNullOrEmpty(projectRoot))
                return assetPath;

            return Path.Combine(projectRoot, assetPath);
        }
    }
}
