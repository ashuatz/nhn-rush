using Rush.Data;
using Rush.Stage;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Rush.EditorTools
{
    /// <summary>
    /// 게임플레이 연출(사거리 표시 / 건설 실루엣 / 선택 링)을 에디트 모드에서 확인하고 조정하는 창.
    ///
    /// 플레이 모드에 들어가지 않아도 슬롯 하나를 골라 실제 셰이더가 어떻게 보이는지 볼 수 있고,
    /// 머티리얼 파라미터를 같은 창에서 바로 만진다.
    /// </summary>
    public class GameplayFxWindow : EditorWindow
    {
        static readonly Color windowBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color panelBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color panelBorderColor = new Color(0.17f, 0.17f, 0.17f, 1f);
        static readonly Color headerBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color accentColor = new Color(0.36f, 0.36f, 0.36f, 1f);
        static readonly Color subtleTextColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        static readonly Color warnTextColor = new Color(1f, 0.6f, 0.2f, 1f);
        static readonly Color okTextColor = new Color(0.45f, 0.85f, 0.5f, 1f);

        const string MaterialDir = "Assets/RushGame/Materials";
        const string GhostRootName = "BuildGhosts";
        const string RangeIndicatorName = "RangeIndicator";
        const string SelectionRingName = "SelectionRing";

        Label _pipelineStatus;
        Button _pipelineFixButton;
        Label _slotInfo;
        VisualElement _materialBody;

        bool _previewEnabled;
        bool _showRange = true;
        bool _showRing = true;
        bool _showGhost;
        float _radius = 4f;
        TowerType _ghostType = TowerType.Archer;

        [MenuItem("Rush/Gameplay FX Preview")]
        public static void Open()
        {
            var window = GetWindow<GameplayFxWindow>();
            window.titleContent = new GUIContent("Gameplay FX");
            window.minSize = new Vector2(400f, 520f);
        }

        void OnDisable()
        {
            // 창을 닫으면 프리뷰 흔적을 씬에 남기지 않는다
            if (!_previewEnabled)
                return;

            _previewEnabled = false;
            ApplyPreview();
        }

        void OnFocus()
        {
            RefreshPipelineStatus();
            RefreshSlotInfo();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.backgroundColor = windowBackground;

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            scroll.contentContainer.style.paddingLeft = 10f;
            scroll.contentContainer.style.paddingRight = 10f;
            scroll.contentContainer.style.paddingTop = 10f;
            scroll.contentContainer.style.paddingBottom = 10f;

            scroll.Add(BuildPipelineSection());
            scroll.Add(BuildPreviewSection());
            scroll.Add(BuildResourceSection());
            scroll.Add(BuildMaterialSection());

            root.Add(scroll);

            RefreshPipelineStatus();
            RefreshSlotInfo();
        }

        // -------------------------------------------------------------------
        // 파이프라인
        // -------------------------------------------------------------------

        VisualElement BuildPipelineSection()
        {
            var section = CreateSectionShell("렌더 파이프라인", "URP", out var body);

            var description = new Label("사거리 표시 셰이더는 씬 뎁스를 읽는다. Depth Texture가 꺼져 있으면 아무것도 그려지지 않는다.");
            description.style.color = subtleTextColor;
            description.style.fontSize = 11;
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = 8f;
            body.Add(description);

            _pipelineStatus = new Label();
            _pipelineStatus.style.fontSize = 11;
            _pipelineStatus.style.whiteSpace = WhiteSpace.Normal;
            _pipelineStatus.style.marginBottom = 6f;
            body.Add(_pipelineStatus);

            _pipelineFixButton = new Button(EnableDepthTexture);
            _pipelineFixButton.text = "Depth Texture 켜기";
            body.Add(_pipelineFixButton);

            return section;
        }

        void RefreshPipelineStatus()
        {
            if (_pipelineStatus == null)
                return;

            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            if (asset == null)
            {
                _pipelineStatus.text = "활성 URP 에셋을 찾지 못했다 (Graphics 설정 확인)";
                _pipelineStatus.style.color = warnTextColor;
                _pipelineFixButton.SetEnabled(false);
                return;
            }

            if (!asset.supportsCameraDepthTexture)
            {
                _pipelineStatus.text = $"{asset.name}: Depth Texture 꺼짐";
                _pipelineStatus.style.color = warnTextColor;
                _pipelineFixButton.SetEnabled(true);
                return;
            }

            _pipelineStatus.text = $"{asset.name}: Depth Texture 켜짐";
            _pipelineStatus.style.color = okTextColor;
            _pipelineFixButton.SetEnabled(false);
        }

        void EnableDepthTexture()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            if (asset == null)
                return;

            var so = new SerializedObject(asset);
            var prop = so.FindProperty("m_RequireDepthTexture");

            if (prop == null)
            {
                Debug.LogWarning("[GameplayFx] m_RequireDepthTexture 프로퍼티를 찾지 못함");
                return;
            }

            prop.boolValue = true;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            RefreshPipelineStatus();
        }

        // -------------------------------------------------------------------
        // 씬 프리뷰
        // -------------------------------------------------------------------

        VisualElement BuildPreviewSection()
        {
            var section = CreateSectionShell("씬 프리뷰", "PREVIEW", out var body);

            _slotInfo = new Label();
            _slotInfo.style.color = subtleTextColor;
            _slotInfo.style.fontSize = 11;
            _slotInfo.style.whiteSpace = WhiteSpace.Normal;
            _slotInfo.style.marginBottom = 8f;
            body.Add(_slotInfo);

            var enableToggle = new Toggle("프리뷰 켜기");
            enableToggle.value = _previewEnabled;
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                _previewEnabled = evt.newValue;
                ApplyPreview();
            });
            body.Add(enableToggle);

            var rangeToggle = new Toggle("사거리 표시");
            rangeToggle.value = _showRange;
            rangeToggle.RegisterValueChangedCallback(evt =>
            {
                _showRange = evt.newValue;
                ApplyPreview();
            });
            body.Add(rangeToggle);

            var radiusRow = new VisualElement();
            radiusRow.style.flexDirection = FlexDirection.Row;
            radiusRow.style.alignItems = Align.Center;
            radiusRow.style.marginBottom = 6f;

            var radiusLabel = new Label("반경");
            radiusLabel.style.width = 120f;
            radiusLabel.style.flexShrink = 0f;
            radiusRow.Add(radiusLabel);

            var radiusSlider = new Slider(0.5f, 14f);
            radiusSlider.value = _radius;
            radiusSlider.showInputField = true;
            radiusSlider.style.flexGrow = 1f;
            radiusSlider.RegisterValueChangedCallback(evt =>
            {
                _radius = evt.newValue;
                ApplyPreview();
            });
            radiusRow.Add(radiusSlider);

            body.Add(radiusRow);

            var ringToggle = new Toggle("선택 링");
            ringToggle.value = _showRing;
            ringToggle.RegisterValueChangedCallback(evt =>
            {
                _showRing = evt.newValue;
                ApplyPreview();
            });
            body.Add(ringToggle);

            var ghostToggle = new Toggle("건설 실루엣");
            ghostToggle.value = _showGhost;
            ghostToggle.RegisterValueChangedCallback(evt =>
            {
                _showGhost = evt.newValue;
                ApplyPreview();
            });
            body.Add(ghostToggle);

            var ghostField = new EnumField("실루엣 타워", _ghostType);
            ghostField.RegisterValueChangedCallback(evt =>
            {
                _ghostType = (TowerType)evt.newValue;
                ApplyPreview();
            });
            body.Add(ghostField);

            var refresh = new Button(() =>
            {
                RefreshSlotInfo();
                ApplyPreview();
            });
            refresh.text = "선택한 슬롯으로 갱신";
            refresh.style.marginTop = 8f;
            body.Add(refresh);

            return section;
        }

        void RefreshSlotInfo()
        {
            if (_slotInfo == null)
                return;

            var slot = ResolveSlot();

            if (slot == null)
            {
                _slotInfo.text = "씬에 TowerSlot이 없다. 먼저 Stage Command Center에서 씬 셋업을 실행할 것.";
                return;
            }

            _slotInfo.text = $"대상 슬롯: {slot.name} (하이어라키에서 슬롯을 선택하면 대상이 바뀐다)";
        }

        /// <summary>하이어라키에서 고른 슬롯을 우선하고, 없으면 씬의 첫 슬롯을 쓴다.</summary>
        static TowerSlot ResolveSlot()
        {
            if (Selection.activeGameObject != null)
            {
                var selected = Selection.activeGameObject.GetComponentInParent<TowerSlot>();

                if (selected != null)
                    return selected;
            }

            var slots = Object.FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);

            if (slots.Length == 0)
                return null;

            return slots[0];
        }

        /// <summary>
        /// 프리뷰를 씬에 반영한다.
        /// 대상 슬롯이 바뀌어도 흔적이 남지 않도록 매번 모든 슬롯을 끈 뒤 대상 하나만 켠다.
        /// </summary>
        void ApplyPreview()
        {
            var slots = Object.FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);

            foreach (var other in slots)
            {
                SetChildActive(other.transform, RangeIndicatorName, false);
                SetChildActive(other.transform, SelectionRingName, false);
            }

            var slot = ResolveSlot();

            if (slot == null)
            {
                ApplyGhostPreview(null);
                SceneView.RepaintAll();
                return;
            }

            var range = slot.transform.Find(RangeIndicatorName);

            if (range != null)
            {
                range.localScale = Vector3.one * (_radius * 2f);
                range.gameObject.SetActive(_previewEnabled && _showRange);
            }

            SetChildActive(slot.transform, SelectionRingName, _previewEnabled && _showRing);

            ApplyGhostPreview(slot);

            SceneView.RepaintAll();
        }

        static void SetChildActive(Transform parent, string childName, bool active)
        {
            var child = parent.Find(childName);

            if (child == null)
                return;

            child.gameObject.SetActive(active);
        }

        /// <summary>
        /// 베이크된 고스트를 슬롯 위로 옮겨 켠다.
        /// 런타임 컴포넌트를 거치지 않고 오브젝트 이름으로 직접 찾는다 (에디트 모드에서는 Awake가 돌지 않음).
        /// </summary>
        void ApplyGhostPreview(TowerSlot slot)
        {
            var root = GameObject.Find(GhostRootName);

            if (root == null)
                return;

            bool show = _previewEnabled && _showGhost && slot != null;
            string targetName = $"Ghost_{_ghostType}";

            foreach (Transform child in root.transform)
            {
                bool match = show && child.name == targetName;

                if (match)
                    child.position = slot.BuildPosition;

                child.gameObject.SetActive(match);
            }
        }

        // -------------------------------------------------------------------
        // 리소스
        // -------------------------------------------------------------------

        VisualElement BuildResourceSection()
        {
            var section = CreateSectionShell("리소스", "ASSETS", out var body);

            body.Add(CreateMaterialStatusRow("Mat_Range", RushSetupActions.RangeSphereShader));
            body.Add(CreateMaterialStatusRow("Mat_SlotRing", RushSetupActions.SelectionRingShader));
            body.Add(CreateMaterialStatusRow("Mat_BuildGhost", RushSetupActions.BuildGhostShader));
            body.Add(CreateMaterialStatusRow("Mat_Debris", RushSetupActions.DebrisChunkShader));
            body.Add(CreateMaterialStatusRow("Mat_SmokePuff", RushSetupActions.SmokePuffShader));

            var refreshSlots = new Button(() =>
            {
                RushSetupActions.RefreshSlotIndicators();
                RefreshSlotInfo();
                ApplyPreview();
            });
            refreshSlots.text = "슬롯 표시 오브젝트 갱신";
            refreshSlots.style.marginTop = 8f;
            body.Add(refreshSlots);

            var slotHint = new Label("예전 씬은 사거리 표시가 원판, 선택 링이 큐브다. 위 버튼으로 구 / 쿼드 규격으로 바꾼다.");
            slotHint.style.color = subtleTextColor;
            slotHint.style.fontSize = 10;
            slotHint.style.whiteSpace = WhiteSpace.Normal;
            slotHint.style.marginTop = 4f;
            body.Add(slotHint);

            var rebake = new Button(() =>
            {
                RushSetupActions.BakeBuildGhosts();
                ApplyPreview();
            });
            rebake.text = "건설 실루엣 다시 베이크";
            rebake.style.marginTop = 8f;
            body.Add(rebake);

            var fxPrefabs = new Button(RushSetupActions.EnsureFxPrefabs);
            fxPrefabs.text = "파티클 연출 프리팹 생성 (사망 파편 / 연기)";
            fxPrefabs.style.marginTop = 8f;
            body.Add(fxPrefabs);

            var hint = new Label("타워 프리팹 비주얼을 바꾼 뒤에는 실루엣을 다시 베이크해야 반영된다.");
            hint.style.color = subtleTextColor;
            hint.style.fontSize = 10;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 4f;
            body.Add(hint);

            return section;
        }

        VisualElement CreateMaterialStatusRow(string materialName, string shaderName)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;

            var label = new Label(materialName);
            label.style.width = 140f;
            label.style.flexShrink = 0f;
            label.style.color = subtleTextColor;
            label.style.fontSize = 11;
            row.Add(label);

            var status = new Label();
            status.style.fontSize = 11;
            status.style.flexGrow = 1f;

            var material = LoadMaterial(materialName);

            if (material == null)
            {
                status.text = "머티리얼 없음";
                status.style.color = warnTextColor;
            }
            else if (material.shader == null || material.shader.name != shaderName)
            {
                status.text = $"셰이더 불일치 ({(material.shader == null ? "없음" : material.shader.name)})";
                status.style.color = warnTextColor;
            }
            else
            {
                status.text = shaderName;
                status.style.color = okTextColor;
            }

            row.Add(status);

            return row;
        }

        static Material LoadMaterial(string materialName)
        {
            return AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{materialName}.mat");
        }

        // -------------------------------------------------------------------
        // 머티리얼 편집
        // -------------------------------------------------------------------

        VisualElement BuildMaterialSection()
        {
            var section = CreateSectionShell("머티리얼", "TUNE", out var body);
            _materialBody = body;

            AddMaterialFoldout("Mat_Range", "RANGE");
            AddMaterialFoldout("Mat_SlotRing", "RING");
            AddMaterialFoldout("Mat_BuildGhost", "GHOST");
            AddMaterialFoldout("Mat_Debris", "DEBRIS");
            AddMaterialFoldout("Mat_SmokePuff", "SMOKE");

            return section;
        }

        void AddMaterialFoldout(string materialName, string badge)
        {
            var material = LoadMaterial(materialName);

            if (material == null)
                return;

            var foldout = new Foldout();
            foldout.text = materialName;
            foldout.value = false;
            StyleFoldout(foldout, badge);

            foldout.Add(new InspectorElement(material));

            _materialBody.Add(foldout);
        }

        // -------------------------------------------------------------------
        // 레이아웃 헬퍼 (StageCommandWindow와 같은 규칙)
        // -------------------------------------------------------------------

        VisualElement CreateSectionShell(string title, string badge, out VisualElement bodyContainer)
        {
            var shell = new VisualElement();
            shell.style.flexDirection = FlexDirection.Column;
            shell.style.backgroundColor = panelBackground;
            shell.style.overflow = Overflow.Hidden;
            shell.style.marginBottom = 10f;

            shell.style.borderLeftWidth = 1f;
            shell.style.borderRightWidth = 1f;
            shell.style.borderTopWidth = 1f;
            shell.style.borderBottomWidth = 1f;
            shell.style.borderLeftColor = panelBorderColor;
            shell.style.borderRightColor = panelBorderColor;
            shell.style.borderTopColor = panelBorderColor;
            shell.style.borderBottomColor = panelBorderColor;

            shell.Add(CreateSectionHeader(title, badge));
            shell.Add(CreateSectionAccentBar());

            bodyContainer = new VisualElement();
            bodyContainer.style.flexGrow = 1f;
            bodyContainer.style.flexDirection = FlexDirection.Column;
            bodyContainer.style.paddingLeft = 14f;
            bodyContainer.style.paddingRight = 14f;
            bodyContainer.style.paddingTop = 12f;
            bodyContainer.style.paddingBottom = 14f;
            bodyContainer.style.backgroundColor = panelBackground;

            shell.Add(bodyContainer);

            return shell;
        }

        VisualElement CreateSectionHeader(string title, string badgeText)
        {
            var header = new VisualElement();
            header.style.height = 40f;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.backgroundColor = headerBackground;
            header.style.paddingLeft = 14f;
            header.style.paddingRight = 14f;

            var leftGroup = new VisualElement();
            leftGroup.style.flexDirection = FlexDirection.Row;
            leftGroup.style.alignItems = Align.Center;

            var accent = new VisualElement();
            accent.style.width = 2f;
            accent.style.height = 18f;
            accent.style.backgroundColor = accentColor;
            accent.style.marginRight = 8f;
            leftGroup.Add(accent);

            var titleLabel = new Label(title);
            titleLabel.style.color = Color.white;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.fontSize = 12;
            leftGroup.Add(titleLabel);

            header.Add(leftGroup);

            if (!string.IsNullOrEmpty(badgeText))
            {
                var badge = new Label(badgeText.ToUpperInvariant());
                badge.style.color = subtleTextColor;
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
            var bar = new VisualElement();
            bar.style.height = 1f;
            bar.style.backgroundColor = panelBorderColor;

            return bar;
        }

        void StyleFoldout(Foldout foldout, string badgeText)
        {
            if (foldout == null)
                return;

            foldout.style.marginBottom = 8f;
            foldout.style.backgroundColor = panelBackground;
            foldout.style.borderLeftWidth = 1f;
            foldout.style.borderRightWidth = 1f;
            foldout.style.borderTopWidth = 1f;
            foldout.style.borderBottomWidth = 1f;
            foldout.style.borderLeftColor = panelBorderColor;
            foldout.style.borderRightColor = panelBorderColor;
            foldout.style.borderTopColor = panelBorderColor;
            foldout.style.borderBottomColor = panelBorderColor;

            var toggle = foldout.Q<Toggle>();

            if (toggle != null)
            {
                toggle.style.backgroundColor = headerBackground;
                toggle.style.height = 26f;
                toggle.style.paddingLeft = 10f;
                toggle.style.paddingRight = 10f;
                toggle.style.alignItems = Align.Center;
                toggle.style.unityFontStyleAndWeight = FontStyle.Bold;
                toggle.style.color = subtleTextColor;

                if (!string.IsNullOrEmpty(badgeText))
                {
                    var badge = new Label(badgeText.ToUpperInvariant());
                    badge.style.color = subtleTextColor;
                    badge.style.fontSize = 9;
                    badge.style.paddingLeft = 8f;
                    badge.style.paddingRight = 8f;
                    toggle.Add(badge);
                }
            }

            var content = foldout.contentContainer;

            if (content != null)
            {
                content.style.paddingLeft = 12f;
                content.style.paddingRight = 12f;
                content.style.paddingTop = 10f;
                content.style.paddingBottom = 12f;
                content.style.backgroundColor = panelBackground;
            }
        }
    }
}
