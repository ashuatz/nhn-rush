using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Common.Rendering.EditorTools
{
    /// <summary>
    /// DOF 포커스 디버그 창.
    ///
    /// 어디가 또렷하게 보이는지 흑백 마스크로 확인하고, 현재 적용 중인
    /// focusDistance / focalLength / aperture 값과 그 값으로 계산된
    /// 포커스 거리 구간을 같이 읽는다.
    ///
    /// 값 편집은 Volume 인스펙터에서 한다. 어느 볼륨에 써야 하는지 추측하지 않기 위함이며,
    /// 대신 DepthOfField를 오버라이드하는 씬 볼륨을 찾아 바로 선택할 수 있게 해둔다.
    /// </summary>
    sealed class DofDebugWindow : EditorWindow
    {
        static readonly Color WindowBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color PanelBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color PanelBorderColor = new Color(0.17f, 0.17f, 0.17f, 1f);
        static readonly Color HeaderBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color AccentColor = new Color(0.36f, 0.36f, 0.36f, 1f);
        static readonly Color SubtleTextColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        static readonly Color InfoAccentColor = new Color(0.4f, 0.6f, 1f, 1f);
        static readonly Color WarnAccentColor = new Color(1f, 0.6f, 0.2f, 1f);

        const float LabelWidth = 130f;

        /// <summary>읽기 전용 값 갱신 주기(ms). 볼륨 블렌딩과 카메라 이동을 따라간다.</summary>
        const long ReadoutIntervalMs = 100;

        Toggle enabledToggle;
        EnumField viewField;
        Slider thresholdSlider;
        Slider darknessSlider;

        Label statusLabel;
        Label dofValuesLabel;
        Label focusRangeLabel;
        VisualElement warningBox;
        Label warningLabel;
        VisualElement volumeListContainer;

        [MenuItem("Common/DOF Focus Debug")]
        public static void Open()
        {
            DofDebugWindow window = GetWindow<DofDebugWindow>("DOF Focus");
            window.minSize = new Vector2(430f, 520f);
        }

        void OnDisable()
        {
            // 화면 전체를 흑백으로 덮는 뷰가 켜진 채 남으면 혼란스럽다.
            // 창 생명주기에 묶어 반드시 끈다.
            DofDebugState.Disable();

            RepaintViews();
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

            scroll.Add(BuildViewSection());
            scroll.Add(BuildReadoutSection());
            scroll.Add(BuildVolumeSection());

            RefreshReadout();

            // 볼륨 값은 렌더링 중에 갱신되므로 주기적으로 다시 읽는다.
            root.schedule.Execute(RefreshReadout).Every(ReadoutIntervalMs);
        }

        // ---------------------------------------------------------------------
        // 섹션 구성
        // ---------------------------------------------------------------------

        VisualElement BuildViewSection()
        {
            VisualElement section = CreateSectionShell("디버그 뷰", "VIEW", out VisualElement body);

            enabledToggle = new Toggle("디버그 뷰 켜기") { value = DofDebugState.Enabled };
            enabledToggle.labelElement.style.minWidth = LabelWidth;
            enabledToggle.RegisterValueChangedCallback(OnEnabledChanged);
            body.Add(enabledToggle);

            body.Add(CreateInfoBox(
                "FocusMask: 흰색 = 포커스가 맞는 영역, 검정 = 흐려지는 영역.\n"
                + "Overlay: 씬 위에 겹쳐 무엇이 포커스인지 형체로 확인.\n"
                + "BlurAmount: 블러 강도 계조 (흰색 = 최대 블러).",
                InfoAccentColor));

            viewField = new EnumField("표시 방식", DofDebugState.View);
            viewField.labelElement.style.minWidth = LabelWidth;
            viewField.RegisterValueChangedCallback(OnViewChanged);
            body.Add(viewField);

            thresholdSlider = new Slider("포커스 임계값",
                DofDebugState.MinFocusThreshold, DofDebugState.MaxFocusThreshold)
            {
                value = DofDebugState.FocusThreshold,
                showInputField = true,
            };
            thresholdSlider.labelElement.style.minWidth = LabelWidth;
            thresholdSlider.style.marginTop = 4f;
            thresholdSlider.RegisterValueChangedCallback(OnThresholdChanged);
            body.Add(thresholdSlider);

            body.Add(CreateHintLabel(
                "임계값 = 포커스로 인정할 CoC(착란원) 크기. 낮추면 흰색 영역이 좁아진다. "
                + "아래 포커스 구간도 이 값 기준으로 계산된다."));

            darknessSlider = new Slider("Overlay 어둡기", 0f, 1f)
            {
                value = DofDebugState.OverlayDarkness,
                showInputField = true,
            };
            darknessSlider.labelElement.style.minWidth = LabelWidth;
            darknessSlider.style.marginTop = 6f;
            darknessSlider.RegisterValueChangedCallback(OnDarknessChanged);
            body.Add(darknessSlider);

            statusLabel = new Label();
            statusLabel.style.color = SubtleTextColor;
            statusLabel.style.fontSize = 10;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusLabel.style.marginTop = 10f;
            body.Add(statusLabel);

            return section;
        }

        VisualElement BuildReadoutSection()
        {
            VisualElement section = CreateSectionShell("적용 중인 DOF 값", "READOUT", out VisualElement body);

            dofValuesLabel = new Label();
            dofValuesLabel.style.color = SubtleTextColor;
            dofValuesLabel.style.fontSize = 11;
            dofValuesLabel.style.whiteSpace = WhiteSpace.Normal;
            body.Add(dofValuesLabel);

            VisualElement rangeBox = new VisualElement();
            rangeBox.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
            rangeBox.style.paddingLeft = 12f;
            rangeBox.style.paddingRight = 12f;
            rangeBox.style.paddingTop = 10f;
            rangeBox.style.paddingBottom = 10f;
            rangeBox.style.borderLeftWidth = 2f;
            rangeBox.style.borderLeftColor = AccentColor;
            rangeBox.style.marginTop = 12f;

            focusRangeLabel = new Label();
            focusRangeLabel.style.color = Color.white;
            focusRangeLabel.style.fontSize = 12;
            focusRangeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            focusRangeLabel.style.whiteSpace = WhiteSpace.Normal;
            rangeBox.Add(focusRangeLabel);

            body.Add(rangeBox);

            warningBox = new VisualElement();
            warningBox.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
            warningBox.style.paddingLeft = 12f;
            warningBox.style.paddingRight = 12f;
            warningBox.style.paddingTop = 10f;
            warningBox.style.paddingBottom = 10f;
            warningBox.style.borderLeftWidth = 2f;
            warningBox.style.borderLeftColor = WarnAccentColor;
            warningBox.style.marginTop = 10f;
            warningBox.style.display = DisplayStyle.None;

            warningLabel = new Label();
            warningLabel.style.color = SubtleTextColor;
            warningLabel.style.fontSize = 11;
            warningLabel.style.whiteSpace = WhiteSpace.Normal;
            warningBox.Add(warningLabel);

            body.Add(warningBox);

            return section;
        }

        VisualElement BuildVolumeSection()
        {
            VisualElement section = CreateSectionShell("DOF 볼륨", "VOLUMES", out VisualElement body);

            body.Add(CreateHintLabel(
                "값 편집은 Volume 인스펙터에서 한다. 아래 버튼으로 해당 볼륨을 바로 선택할 수 있다."));

            volumeListContainer = new VisualElement();
            volumeListContainer.style.marginTop = 8f;
            body.Add(volumeListContainer);

            Button refreshButton = new Button(RefreshVolumeList) { text = "볼륨 다시 찾기" };
            refreshButton.style.marginTop = 8f;
            body.Add(refreshButton);

            RefreshVolumeList();

            return section;
        }

        // ---------------------------------------------------------------------
        // 상태 변경
        // ---------------------------------------------------------------------

        void OnEnabledChanged(ChangeEvent<bool> evt)
        {
            DofDebugState.Enabled = evt.newValue;

            if (!evt.newValue)
                DofDebugState.LastRenderedFrame = -1;

            RepaintViews();
        }

        void OnViewChanged(ChangeEvent<System.Enum> evt)
        {
            DofDebugState.View = (DofDebugView)evt.newValue;
            RepaintViews();
        }

        void OnThresholdChanged(ChangeEvent<float> evt)
        {
            DofDebugState.FocusThreshold = evt.newValue;

            RefreshReadout();
            RepaintViews();
        }

        void OnDarknessChanged(ChangeEvent<float> evt)
        {
            DofDebugState.OverlayDarkness = evt.newValue;
            RepaintViews();
        }

        static void RepaintViews()
        {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        // ---------------------------------------------------------------------
        // 읽기 전용 표시 갱신
        // ---------------------------------------------------------------------

        void RefreshReadout()
        {
            RefreshStatus();

            if (!DofDebugMath.TryGetParams(out DofDebugParams dofParams))
            {
                dofValuesLabel.text = "적용 중인 DepthOfField 오버라이드가 없다.";
                focusRangeLabel.text = "포커스 구간: -";

                ShowWarning("씬 Volume 프로파일에 DepthOfField를 추가하고 Mode를 Bokeh 또는 Gaussian으로 바꾼다. "
                            + "Mode가 Off면 디버그할 값이 없다.");
                return;
            }

            if (dofParams.IsBokeh())
                RefreshBokehReadout(dofParams);
            else
                RefreshGaussianReadout(dofParams);
        }

        void RefreshBokehReadout(DofDebugParams dofParams)
        {
            dofValuesLabel.text =
                $"Mode: Bokeh\n"
                + $"Focus Distance: {dofParams.focusDistance:F2} m\n"
                + $"Max CoC: {dofParams.maxCoC:F4}";

            if (dofParams.HasDegenerateBokehCoC())
            {
                focusRangeLabel.text = "포커스 구간: 계산 불가";

                ShowWarning("Max CoC가 0 이하다. Focus Distance가 Focal Length/1000 보다 작거나 같아 "
                            + "CoC 부호가 뒤집힌 상태다. Focus Distance를 키우거나 Focal Length를 줄인다.");
                return;
            }

            UpdateFocusRangeLabel(dofParams);
        }

        void RefreshGaussianReadout(DofDebugParams dofParams)
        {
            dofValuesLabel.text =
                $"Mode: Gaussian (원경만 블러)\n"
                + $"Gaussian Start: {dofParams.gaussianStart:F2} m\n"
                + $"Gaussian End: {dofParams.gaussianEnd:F2} m";

            UpdateFocusRangeLabel(dofParams);

            ShowWarning("Gaussian 모드에는 Focus Distance / Focal Length가 없다. "
                        + "그 값으로 조절하려면 Mode를 Bokeh로 바꾼다.");
        }

        void UpdateFocusRangeLabel(DofDebugParams dofParams)
        {
            if (!DofDebugMath.TryComputeFocusRange(dofParams, DofDebugState.FocusThreshold,
                    out float nearLimit, out float farLimit))
            {
                focusRangeLabel.text = "포커스 구간: 계산 불가";
                return;
            }

            string farText = "무한";

            if (!float.IsPositiveInfinity(farLimit))
                farText = $"{farLimit:F2} m";

            focusRangeLabel.text = $"포커스 구간: {nearLimit:F2} m ~ {farText}";

            HideWarning();
        }

        void RefreshStatus()
        {
            if (!DofDebugState.Enabled)
            {
                statusLabel.text = "디버그 뷰 꺼짐. 창을 닫으면 자동으로 꺼진다.";
                return;
            }

            if (DofDebugState.IsRenderingLive())
            {
                statusLabel.text = "그려지고 있음.";
                return;
            }

            statusLabel.text = "켜져 있지만 그려지지 않았다. 렌더러 에셋(Assets/Settings/*_Renderer.asset)에 "
                               + "'DOF Focus Debug' 피처를 추가했는지 확인한다. "
                               + "씬 뷰나 게임 뷰가 갱신되지 않는 상태여도 이렇게 표시된다.";
        }

        void ShowWarning(string message)
        {
            warningLabel.text = message;
            warningBox.style.display = DisplayStyle.Flex;
        }

        void HideWarning()
        {
            warningBox.style.display = DisplayStyle.None;
        }

        // ---------------------------------------------------------------------
        // 볼륨 목록
        // ---------------------------------------------------------------------

        void RefreshVolumeList()
        {
            volumeListContainer.Clear();

            List<Volume> found = FindVolumesWithDepthOfField();

            if (found.Count == 0)
            {
                volumeListContainer.Add(CreateHintLabel("DepthOfField를 오버라이드하는 씬 볼륨을 찾지 못했다."));
                return;
            }

            foreach (Volume volume in found)
                volumeListContainer.Add(CreateVolumeRow(volume));
        }

        /// <summary>
        /// DepthOfField를 오버라이드하는 씬 볼륨들.
        /// profile이 아니라 sharedProfile을 읽는다. profile 게터는 런타임 사본을 만들기 때문이다.
        /// </summary>
        static List<Volume> FindVolumesWithDepthOfField()
        {
            List<Volume> result = new List<Volume>();

            Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);

            foreach (Volume volume in volumes)
            {
                VolumeProfile profile = volume.sharedProfile;

                if (profile == null)
                    continue;

                if (!profile.TryGet(out DepthOfField _))
                    continue;

                result.Add(volume);
            }

            return result;
        }

        VisualElement CreateVolumeRow(Volume volume)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;

            Label nameLabel = new Label(volume.name);
            nameLabel.style.color = SubtleTextColor;
            nameLabel.style.fontSize = 11;
            nameLabel.style.flexGrow = 1f;
            row.Add(nameLabel);

            Button selectButton = new Button(() => SelectVolume(volume)) { text = "선택" };
            selectButton.style.width = 70f;
            row.Add(selectButton);

            return row;
        }

        static void SelectVolume(Volume volume)
        {
            if (volume == null)
                return;

            Selection.activeObject = volume.gameObject;
            EditorGUIUtility.PingObject(volume.gameObject);
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

        VisualElement CreateInfoBox(string message, Color accent)
        {
            VisualElement box = new VisualElement();
            box.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
            box.style.paddingLeft = 12f;
            box.style.paddingRight = 12f;
            box.style.paddingTop = 10f;
            box.style.paddingBottom = 10f;
            box.style.borderLeftWidth = 2f;
            box.style.borderLeftColor = accent;
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
    }
}
