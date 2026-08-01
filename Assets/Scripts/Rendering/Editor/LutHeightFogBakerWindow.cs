using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Common.Rendering.EditorTools
{
    /// <summary>
    /// 거리/높이 그라디언트를 편집해 2D 포그 LUT 텍스처로 굽는 창.
    ///
    /// 런타임 오브젝트를 만들지 않고 에디트 모드에서 룩을 확정한 뒤 텍스처로 베이크한다.
    /// 구운 텍스처에는 그라디언트가 함께 실려 다시 열어 편집할 수 있다.
    /// </summary>
    sealed class LutHeightFogBakerWindow : EditorWindow
    {
        static readonly Color WindowBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color PanelBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color PanelBorderColor = new Color(0.17f, 0.17f, 0.17f, 1f);
        static readonly Color HeaderBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color AccentColor = new Color(0.36f, 0.36f, 0.36f, 1f);
        static readonly Color SubtleTextColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        static readonly Color InfoAccentColor = new Color(0.4f, 0.6f, 1f, 1f);

        const float LabelWidth = 120f;
        const int PreviewWidth = 256;
        const int PreviewHeight = 64;
        const int CheckerCellSize = 8;

        /// <summary>편집 대상 LUT. 에셋 참조라 도메인 리로드를 견딘다.</summary>
        [SerializeField] Texture2D targetLut;

        FogLutGradientData gradientData;
        Texture2D previewTexture;

        /// <summary>대상이 이 창에서 구운 PNG LUT인지. 아니면 덮어쓰기를 막는다.</summary>
        bool targetIsOwnedLut;

        ObjectField targetField;
        GradientField distanceField;
        GradientField heightField;
        Image previewImage;
        Label statusLabel;
        Button rebakeButton;
        Button reloadButton;

        [MenuItem("Common/LUT Height Fog Baker")]
        public static void Open()
        {
            LutHeightFogBakerWindow window = GetWindow<LutHeightFogBakerWindow>("Fog LUT Baker");
            window.minSize = new Vector2(420f, 560f);
        }

        void OnEnable()
        {
            gradientData = CreateInstance<FogLutGradientData>();
            gradientData.hideFlags = HideFlags.HideAndDontSave;

            // 창을 다시 열거나 스크립트가 리로드되면 대상 텍스처에서 편집 상태를 복구한다.
            targetIsOwnedLut = FogLutBaker.TryLoadGradients(targetLut, gradientData) && IsOwnedLutPath(targetLut);

            if (!targetIsOwnedLut)
                gradientData.ResetToDefault();

            previewTexture = new Texture2D(PreviewWidth, PreviewHeight, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        void OnDisable()
        {
            // 임시 상태는 창 생명주기에 묶어 둔다.
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }

            if (gradientData != null)
            {
                DestroyImmediate(gradientData);
                gradientData = null;
            }
        }

        void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.backgroundColor = WindowBackground;

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.contentContainer.style.paddingLeft = 10f;
            scroll.contentContainer.style.paddingRight = 10f;
            scroll.contentContainer.style.paddingTop = 10f;
            scroll.contentContainer.style.paddingBottom = 10f;
            root.Add(scroll);

            scroll.Add(BuildSourceSection());
            scroll.Add(BuildPreviewSection());
            scroll.Add(BuildOutputSection());

            RefreshPreview();
            RefreshButtonStates();
        }

        // ---------------------------------------------------------------------
        // 섹션 구성
        // ---------------------------------------------------------------------

        VisualElement BuildSourceSection()
        {
            VisualElement section = CreateSectionShell("LUT 소스", "SOURCE", out VisualElement body);

            targetField = new ObjectField("대상 LUT")
            {
                objectType = typeof(Texture2D),
                allowSceneObjects = false,
                value = targetLut,
            };
            targetField.labelElement.style.minWidth = LabelWidth;
            targetField.RegisterValueChangedCallback(OnTargetChanged);
            body.Add(targetField);

            body.Add(CreateInfoBox(
                "비워두면 새 LUT을 만들 수 있다. 이 창에서 구운 텍스처를 넣으면 "
                + "그라디언트가 그대로 복원되어 이어서 편집할 수 있다."));

            distanceField = new GradientField("거리 램프 (U)")
            {
                value = gradientData.distanceGradient,
            };
            distanceField.labelElement.style.minWidth = LabelWidth;
            distanceField.style.marginBottom = 4f;
            distanceField.RegisterValueChangedCallback(OnDistanceGradientChanged);
            body.Add(distanceField);

            heightField = new GradientField("높이 램프 (V)")
            {
                value = gradientData.heightGradient,
            };
            heightField.labelElement.style.minWidth = LabelWidth;
            heightField.RegisterValueChangedCallback(OnHeightGradientChanged);
            body.Add(heightField);

            body.Add(CreateHintLabel(
                "LUT = 거리 램프 * 높이 램프 (알파 포함 성분별 곱). "
                + "높이 램프를 흰색/불투명으로 두면 순수 거리 포그가 된다."));

            return section;
        }

        VisualElement BuildPreviewSection()
        {
            VisualElement section = CreateSectionShell("프리뷰", "PREVIEW", out VisualElement body);

            body.Add(CreateHintLabel("가로 = 카메라 거리 (좌: 가까움), 세로 = 월드 높이 (아래: 낮음). 체커는 씬 컬러를 대신한다."));

            previewImage = new Image
            {
                scaleMode = ScaleMode.StretchToFill,
                image = previewTexture,
            };
            previewImage.style.height = 150f;
            previewImage.style.marginTop = 6f;
            previewImage.style.borderLeftWidth = 1f;
            previewImage.style.borderRightWidth = 1f;
            previewImage.style.borderTopWidth = 1f;
            previewImage.style.borderBottomWidth = 1f;
            previewImage.style.borderLeftColor = PanelBorderColor;
            previewImage.style.borderRightColor = PanelBorderColor;
            previewImage.style.borderTopColor = PanelBorderColor;
            previewImage.style.borderBottomColor = PanelBorderColor;
            body.Add(previewImage);

            VisualElement axisRow = new VisualElement();
            axisRow.style.flexDirection = FlexDirection.Row;
            axisRow.style.justifyContent = Justify.SpaceBetween;
            axisRow.style.marginTop = 4f;

            axisRow.Add(CreateAxisLabel("가까움"));
            axisRow.Add(CreateAxisLabel("멀어짐"));

            body.Add(axisRow);

            return section;
        }

        VisualElement BuildOutputSection()
        {
            VisualElement section = CreateSectionShell("출력", "OUTPUT", out VisualElement body);

            statusLabel = new Label();
            statusLabel.style.color = SubtleTextColor;
            statusLabel.style.fontSize = 11;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusLabel.style.marginBottom = 10f;
            body.Add(statusLabel);

            VisualElement buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;

            Button createButton = new Button(CreateNewLut) { text = "새 LUT 만들기" };
            createButton.style.flexGrow = 1f;
            buttonRow.Add(createButton);

            rebakeButton = new Button(RebakeTargetLut) { text = "다시 굽기" };
            rebakeButton.style.flexGrow = 1f;
            buttonRow.Add(rebakeButton);

            body.Add(buttonRow);

            VisualElement secondaryRow = new VisualElement();
            secondaryRow.style.flexDirection = FlexDirection.Row;
            secondaryRow.style.marginTop = 4f;

            reloadButton = new Button(ReloadFromTarget) { text = "텍스처에서 불러오기" };
            reloadButton.style.flexGrow = 1f;
            secondaryRow.Add(reloadButton);

            Button resetButton = new Button(ResetGradients) { text = "기본값" };
            resetButton.style.flexGrow = 1f;
            secondaryRow.Add(resetButton);

            body.Add(secondaryRow);

            body.Add(CreateHintLabel(
                $"{FogLutBaker.LutWidth}x{FogLutBaker.LutHeight} PNG, 무압축 sRGB, 밉맵 없음, Clamp. "
                + "구운 LUT을 볼륨의 LUT Height Fog > Fog Lut에 지정한다."));

            return section;
        }

        // ---------------------------------------------------------------------
        // 동작
        // ---------------------------------------------------------------------

        void OnTargetChanged(ChangeEvent<Object> evt)
        {
            targetLut = evt.newValue as Texture2D;
            targetIsOwnedLut = FogLutBaker.TryLoadGradients(targetLut, gradientData) && IsOwnedLutPath(targetLut);

            // 대상이 바뀌면 그 텍스처의 편집 상태를 따라간다.
            if (targetIsOwnedLut)
                PushGradientsToFields();

            RefreshPreview();
            RefreshButtonStates();
        }

        /// <summary>
        /// PNG만 굽기 때문에 다른 확장자는 대상으로 인정하지 않는다.
        /// PNG 바이트를 .tga 등에 쓰면 그 에셋이 깨진다.
        /// </summary>
        static bool IsOwnedLutPath(Texture2D lut)
        {
            if (lut == null)
                return false;

            return FogLutBaker.IsBakeablePath(AssetDatabase.GetAssetPath(lut));
        }

        void OnDistanceGradientChanged(ChangeEvent<Gradient> evt)
        {
            gradientData.distanceGradient = evt.newValue;
            RefreshPreview();
        }

        void OnHeightGradientChanged(ChangeEvent<Gradient> evt)
        {
            gradientData.heightGradient = evt.newValue;
            RefreshPreview();
        }

        void CreateNewLut()
        {
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "포그 LUT 저장",
                "FogHeightLut",
                "png",
                "2D 포그 LUT을 저장할 위치를 고른다.");

            if (string.IsNullOrEmpty(assetPath))
                return;

            BakeTo(assetPath);
        }

        void RebakeTargetLut()
        {
            if (!targetIsOwnedLut)
                return;

            string assetPath = AssetDatabase.GetAssetPath(targetLut);

            if (string.IsNullOrEmpty(assetPath))
                return;

            BakeTo(assetPath);
        }

        void BakeTo(string assetPath)
        {
            Texture2D baked = FogLutBaker.Bake(gradientData, assetPath);

            if (baked == null)
            {
                statusLabel.text = $"베이크 실패: {assetPath}";
                return;
            }

            targetLut = baked;
            targetIsOwnedLut = true;
            targetField.SetValueWithoutNotify(baked);

            EditorGUIUtility.PingObject(baked);
            RefreshButtonStates();

            statusLabel.text = $"저장됨: {assetPath}";
        }

        void ReloadFromTarget()
        {
            if (!targetIsOwnedLut)
                return;

            if (!FogLutBaker.TryLoadGradients(targetLut, gradientData))
            {
                statusLabel.text = "이 텍스처에는 그라디언트 정보가 없다. 이 창에서 구운 LUT만 다시 편집할 수 있다.";
                return;
            }

            PushGradientsToFields();
            RefreshPreview();

            statusLabel.text = $"불러옴: {AssetDatabase.GetAssetPath(targetLut)}";
        }

        void ResetGradients()
        {
            gradientData.ResetToDefault();
            PushGradientsToFields();
            RefreshPreview();

            statusLabel.text = "기본 그라디언트로 되돌렸다. 저장하려면 다시 굽는다.";
        }

        void PushGradientsToFields()
        {
            distanceField.SetValueWithoutNotify(gradientData.distanceGradient);
            heightField.SetValueWithoutNotify(gradientData.heightGradient);
        }

        void RefreshButtonStates()
        {
            // 이 창이 구운 PNG LUT만 덮어쓴다. 임의 텍스처를 파괴하지 않기 위한 가드.
            rebakeButton.SetEnabled(targetIsOwnedLut);
            reloadButton.SetEnabled(targetIsOwnedLut);

            if (targetLut == null)
            {
                statusLabel.text = "대상 LUT이 없다. '새 LUT 만들기'로 텍스처를 생성한다.";
                return;
            }

            if (!targetIsOwnedLut)
            {
                statusLabel.text = $"이 창에서 구운 LUT이 아니다 (PNG + 그라디언트 정보 필요): "
                                   + $"{AssetDatabase.GetAssetPath(targetLut)}. 덮어쓰기를 막았다. "
                                   + "'새 LUT 만들기'로 별도 텍스처를 생성한다.";
                return;
            }

            statusLabel.text = $"대상: {AssetDatabase.GetAssetPath(targetLut)}";
        }

        /// <summary>
        /// LUT을 체커보드 위에 합성해 프리뷰한다. 셰이더의 lerp(씬컬러, LUT색, LUT알파)와 같은 식.
        /// </summary>
        void RefreshPreview()
        {
            if (previewTexture == null)
                return;

            Color32[] lutPixels = FogLutBaker.EvaluatePixels(gradientData, PreviewWidth, PreviewHeight);
            Color32[] composited = new Color32[lutPixels.Length];

            for (int y = 0; y < PreviewHeight; y++)
            {
                int rowOffset = y * PreviewWidth;

                for (int x = 0; x < PreviewWidth; x++)
                {
                    int index = rowOffset + x;
                    Color fog = lutPixels[index];
                    Color background = GetCheckerColor(x, y);

                    Color blended = Color.Lerp(background, fog, fog.a);
                    blended.a = 1f;

                    composited[index] = blended;
                }
            }

            previewTexture.SetPixels32(composited);
            previewTexture.Apply(false);

            // Image가 같은 텍스처를 들고 있어도 갱신을 알려야 다시 그린다.
            previewImage.MarkDirtyRepaint();
        }

        static Color GetCheckerColor(int x, int y)
        {
            bool isLightCell = ((x / CheckerCellSize) + (y / CheckerCellSize)) % 2 == 0;

            if (isLightCell)
                return new Color(0.62f, 0.62f, 0.62f, 1f);

            return new Color(0.42f, 0.42f, 0.42f, 1f);
        }

        // ---------------------------------------------------------------------
        // 레이아웃 헬퍼
        // ---------------------------------------------------------------------

        VisualElement CreateSectionShell(string title, string badge, out VisualElement bodyContainer)
        {
            VisualElement shell = new VisualElement();
            shell.style.flexDirection = FlexDirection.Column;
            shell.style.backgroundColor = PanelBackground;
            shell.style.overflow = Overflow.Hidden;
            shell.style.marginBottom = 10f;

            shell.style.borderLeftWidth = 1f;
            shell.style.borderRightWidth = 1f;
            shell.style.borderTopWidth = 1f;
            shell.style.borderBottomWidth = 1f;
            shell.style.borderLeftColor = PanelBorderColor;
            shell.style.borderRightColor = PanelBorderColor;
            shell.style.borderTopColor = PanelBorderColor;
            shell.style.borderBottomColor = PanelBorderColor;

            shell.Add(CreateSectionHeader(title, badge));
            shell.Add(CreateSectionAccentBar());

            bodyContainer = new VisualElement();
            bodyContainer.style.flexGrow = 1f;
            bodyContainer.style.flexDirection = FlexDirection.Column;
            bodyContainer.style.paddingLeft = 14f;
            bodyContainer.style.paddingRight = 14f;
            bodyContainer.style.paddingTop = 12f;
            bodyContainer.style.paddingBottom = 14f;
            bodyContainer.style.backgroundColor = PanelBackground;

            shell.Add(bodyContainer);

            return shell;
        }

        VisualElement CreateSectionHeader(string title, string badgeText)
        {
            VisualElement header = new VisualElement();
            header.style.height = 40f;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.backgroundColor = HeaderBackground;
            header.style.paddingLeft = 14f;
            header.style.paddingRight = 14f;

            VisualElement leftGroup = new VisualElement();
            leftGroup.style.flexDirection = FlexDirection.Row;
            leftGroup.style.alignItems = Align.Center;

            VisualElement accent = new VisualElement();
            accent.style.width = 2f;
            accent.style.height = 18f;
            accent.style.backgroundColor = AccentColor;
            accent.style.marginRight = 8f;
            leftGroup.Add(accent);

            Label titleLabel = new Label(title);
            titleLabel.style.color = Color.white;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.fontSize = 12;
            leftGroup.Add(titleLabel);

            header.Add(leftGroup);

            if (!string.IsNullOrEmpty(badgeText))
            {
                Label badge = new Label(badgeText.ToUpperInvariant());
                badge.style.color = SubtleTextColor;
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                badge.style.fontSize = 9;
                badge.style.paddingLeft = 8f;
                badge.style.paddingRight = 8f;
                badge.style.paddingTop = 2f;
                badge.style.paddingBottom = 2f;
                header.Add(badge);
            }

            return header;
        }

        VisualElement CreateSectionAccentBar()
        {
            VisualElement bar = new VisualElement();
            bar.style.height = 1f;
            bar.style.backgroundColor = PanelBorderColor;

            return bar;
        }

        VisualElement CreateInfoBox(string message)
        {
            VisualElement box = new VisualElement();
            box.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
            box.style.paddingLeft = 12f;
            box.style.paddingRight = 12f;
            box.style.paddingTop = 10f;
            box.style.paddingBottom = 10f;
            box.style.borderLeftWidth = 2f;
            box.style.borderLeftColor = InfoAccentColor;
            box.style.marginTop = 8f;
            box.style.marginBottom = 12f;

            Label label = new Label(message);
            label.style.color = SubtleTextColor;
            label.style.fontSize = 11;
            label.style.whiteSpace = WhiteSpace.Normal;
            box.Add(label);

            return box;
        }

        Label CreateHintLabel(string message)
        {
            Label label = new Label(message);
            label.style.color = SubtleTextColor;
            label.style.fontSize = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 8f;

            return label;
        }

        Label CreateAxisLabel(string text)
        {
            Label label = new Label(text);
            label.style.color = SubtleTextColor;
            label.style.fontSize = 9;

            return label;
        }
    }
}
