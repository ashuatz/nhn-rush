using System.Collections.Generic;
using Rush.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.EditorTools
{
    /// <summary>
    /// 밸런스 전용 윈도우. 보상 플로우/카드의 빠른 편집과 함께
    /// Assets/RushGame/Data 하위 데이터 에셋 전체(타워/몬스터/스테이지/난이도)를 한 화면에서 조절한다.
    /// 플레이 중에 바꿔도 다음 판정부터 바로 반영되며(시스템이 매번 에셋을 읽는다),
    /// 플레이 중 변경분은 플레이를 끝낼 때 디스크에 저장된다.
    /// </summary>
    public class BalanceBoardWindow : EditorWindow
    {
        const string DataRoot = "Assets/RushGame/Data";
        const string RewardFolder = "Assets/RushGame/Data/Rewards";

        static readonly Color windowBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color panelBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
        static readonly Color panelBorderColor = new Color(0.17f, 0.17f, 0.17f, 1f);
        static readonly Color subtleTextColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        static readonly Color disabledTextColor = new Color(0.55f, 0.45f, 0.45f, 1f);

        readonly List<RewardDefinition> _cards = new List<RewardDefinition>();

        VisualElement _cardList;
        DropdownField _rarityFilter;
        TextField _search;
        Label _summary;
        bool _building;

        VisualElement _assetList;
        TextField _assetSearch;
        Label _assetSummary;

        /// <summary>폴더 그룹의 펼침 상태. 목록을 다시 그려도 유지한다.</summary>
        readonly HashSet<string> _collapsedGroups = new HashSet<string>();

        /// <summary>
        /// 플레이 중 이 창에서 편집한 에셋의 GUID. 창 인스턴스가 아니라 static으로 들고,
        /// 에디터 로드 시 등록되는 정적 훅이 플레이 종료 시점에 저장한다.
        /// 창을 닫아도 저장이 유실되지 않는다. 다른 창에서 더럽힌 에셋까지 건드리지 않도록 대상만 기억한다.
        /// </summary>
        static readonly HashSet<string> s_pendingGuids = new HashSet<string>();

        [InitializeOnLoadMethod]
        static void HookPlayModeSave()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode)
                    SavePendingStatic();
            };
        }

        static void SavePendingStatic()
        {
            if (s_pendingGuids.Count == 0)
                return;

            int saved = 0;

            foreach (var guid in s_pendingGuids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));

                if (asset == null)
                    continue;

                AssetDatabase.SaveAssetIfDirty(asset);
                saved++;
            }

            s_pendingGuids.Clear();

            Debug.Log($"[Rush] 플레이 중 조정한 밸런스 수치 {saved}건을 에셋에 저장함");
        }

        [MenuItem("Rush/Balance Board")]
        public static void Open()
        {
            var window = GetWindow<BalanceBoardWindow>();
            window.titleContent = new GUIContent("Balance Board");
            window.minSize = new Vector2(560f, 480f);
        }

        void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            // 저장은 static 훅이 담당한다. 여기서는 UI만 최신 값으로 다시 그린다.
            if (change != PlayModeStateChange.EnteredEditMode)
                return;

            RebuildCardList();
            RebuildAssetList();
        }

        void MarkDirty(UnityEngine.Object asset)
        {
            if (_building)
                return;

            // 목록을 만든 뒤 삭제/재임포트된 대상일 수 있다
            if (asset == null)
                return;

            EditorUtility.SetDirty(asset);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

                if (!string.IsNullOrEmpty(guid))
                    s_pendingGuids.Add(guid);

                return;
            }

            AssetDatabase.SaveAssetIfDirty(asset);
        }

        static RewardFlowConfig LoadConfig()
        {
            return AssetDatabase.LoadAssetAtPath<RewardFlowConfig>($"{RewardFolder}/RewardFlowConfig.asset");
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
            root.Add(scroll);

            scroll.Add(BuildFlowSection());
            scroll.Add(BuildCardSection());
            scroll.Add(BuildAssetSection());

            RebuildCardList();
            RebuildAssetList();
        }

        // ---------- 플로우 수치 ----------

        VisualElement BuildFlowSection()
        {
            var section = Shell("보상 플로우", out var body);

            var config = LoadConfig();

            if (config == null)
            {
                body.Add(Note("RewardFlowConfig가 없음 - Stage Command Center에서 '보상 데이터' 실행"));
                return section;
            }

            body.Add(IntRow("첫 보상 웨이브", config.FirstRewardWave, v => { config.FirstRewardWave = Mathf.Max(1, v); MarkDirty(config); }));
            body.Add(IntRow("N웨이브마다", config.EveryNWaves, v => { config.EveryNWaves = Mathf.Max(1, v); MarkDirty(config); }));
            body.Add(IntRow("제시 카드 수", config.CardsPerOffer, v => { config.CardsPerOffer = Mathf.Clamp(v, 1, 5); MarkDirty(config); }));
            body.Add(IntRow("다시뽑기 (판 전체)", config.RerollsPerRun, v => { config.RerollsPerRun = Mathf.Max(0, v); MarkDirty(config); }));
            body.Add(IntRow("다시뽑기 비용", config.RerollCost, v => { config.RerollCost = Mathf.Max(0, v); MarkDirty(config); }));

            body.Add(Note("등급 목표 확률 % (일반/희귀/영웅/전설) - 카드 가중치 = 목표 확률 / 등급 풀 개수"));

            var weightRow = new VisualElement();
            weightRow.style.flexDirection = FlexDirection.Row;

            weightRow.Add(WeightField(config.TargetCommon, v => { config.TargetCommon = v; MarkDirty(config); }));
            weightRow.Add(WeightField(config.TargetRare, v => { config.TargetRare = v; MarkDirty(config); }));
            weightRow.Add(WeightField(config.TargetHeroic, v => { config.TargetHeroic = v; MarkDirty(config); }));
            weightRow.Add(WeightField(config.TargetLegendary, v => { config.TargetLegendary = v; MarkDirty(config); }));

            body.Add(weightRow);

            return section;
        }

        FloatField WeightField(float value, System.Action<float> onChanged)
        {
            var field = new FloatField();
            field.value = value;
            field.style.flexGrow = 1f;
            field.style.marginRight = 4f;
            field.RegisterValueChangedCallback(evt => onChanged(Mathf.Max(0f, evt.newValue)));

            return field;
        }

        // ---------- 카드 목록 ----------

        VisualElement BuildCardSection()
        {
            var section = Shell("보상 카드 (57종)", out var body);

            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.marginBottom = 6f;

            _rarityFilter = new DropdownField(new List<string> { "전체", "일반", "희귀", "영웅", "전설", "비활성" }, 0);
            _rarityFilter.style.width = 110f;
            _rarityFilter.RegisterValueChangedCallback(evt => RebuildCardList());
            filterRow.Add(_rarityFilter);

            _search = new TextField();
            _search.style.flexGrow = 1f;
            _search.style.marginLeft = 6f;
            _search.RegisterValueChangedCallback(evt => RebuildCardList());
            filterRow.Add(_search);

            body.Add(filterRow);

            _summary = Note("");
            body.Add(_summary);

            _cardList = new VisualElement();
            body.Add(_cardList);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.marginTop = 8f;

            var resetButton = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("수치 초기화",
                    "모든 카드 수치를 카탈로그(시트) 기본값으로 되돌립니다. 조정한 값은 사라집니다.", "초기화", "취소"))
                    return;

                RushSetupActions.CreateRewardAssets(forceValues: true);
                RebuildCardList();
            });
            resetButton.text = "시트 기본값으로 초기화";
            resetButton.style.flexGrow = 1f;
            footer.Add(resetButton);

            body.Add(footer);

            return section;
        }

        void RebuildCardList()
        {
            if (_cardList == null)
                return;

            _building = true;

            try
            {
                _cardList.Clear();
                LoadCards();

                string search = _search != null ? _search.value : "";
                int filterIndex = _rarityFilter != null ? _rarityFilter.index : 0;

                int shown = 0;
                int disabled = 0;

                foreach (var card in _cards)
                {
                    if (card == null)
                        continue;

                    if (!card.Enabled)
                        disabled++;

                    if (!PassesFilter(card, filterIndex, search))
                        continue;

                    _cardList.Add(BuildCardRow(card));
                    shown++;
                }

                if (_summary != null)
                    _summary.text = $"{shown}종 표시 / 전체 {_cards.Count}종 (비활성 {disabled}종)";
            }
            finally
            {
                _building = false;
            }
        }

        void LoadCards()
        {
            _cards.Clear();

            var guids = AssetDatabase.FindAssets("t:RewardDefinition", new[] { RewardFolder });

            foreach (var guid in guids)
            {
                var card = AssetDatabase.LoadAssetAtPath<RewardDefinition>(AssetDatabase.GUIDToAssetPath(guid));

                if (card != null)
                    _cards.Add(card);
            }

            _cards.Sort((a, b) =>
            {
                int byRarity = a.Rarity.CompareTo(b.Rarity);

                if (byRarity != 0)
                    return byRarity;

                return string.CompareOrdinal(a.Id, b.Id);
            });
        }

        static bool PassesFilter(RewardDefinition card, int filterIndex, string search)
        {
            if (filterIndex == 5 && card.Enabled)
                return false;

            if (filterIndex >= 1 && filterIndex <= 4 && (int)card.Rarity != filterIndex - 1)
                return false;

            if (string.IsNullOrEmpty(search))
                return true;

            if (card.Id.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (card.DisplayName.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        static Color RarityColor(RewardRarity rarity)
        {
            if (rarity == RewardRarity.Common)
                return new Color(0.7f, 0.7f, 0.7f, 1f);

            if (rarity == RewardRarity.Rare)
                return new Color(0.4f, 0.65f, 1f, 1f);

            if (rarity == RewardRarity.Heroic)
                return new Color(0.75f, 0.45f, 1f, 1f);

            return new Color(1f, 0.65f, 0.25f, 1f);
        }

        VisualElement BuildCardRow(RewardDefinition card)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2f;
            row.style.paddingTop = 2f;
            row.style.paddingBottom = 2f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = panelBorderColor;

            var enabledToggle = new Toggle();
            enabledToggle.value = card.Enabled;
            enabledToggle.style.width = 18f;
            enabledToggle.tooltip = string.IsNullOrEmpty(card.DisabledReason) ? "덱 포함 여부" : card.DisabledReason;
            enabledToggle.RegisterValueChangedCallback(evt =>
            {
                card.Enabled = evt.newValue;
                MarkDirty(card);
            });
            row.Add(enabledToggle);

            var name = new Label($"{card.Id} {card.DisplayName}");
            name.style.width = 150f;
            name.style.fontSize = 11;
            name.style.color = card.Enabled ? RarityColor(card.Rarity) : disabledTextColor;
            name.tooltip = card.Description;
            name.RegisterCallback<ClickEvent>(evt =>
            {
                Selection.activeObject = card;
                EditorGUIUtility.PingObject(card);
            });
            row.Add(name);

            row.Add(NumberField("V", card.Value, v => { card.Value = v; MarkDirty(card); }));
            row.Add(NumberField("V2", card.Value2, v => { card.Value2 = v; MarkDirty(card); }));
            row.Add(NumberField("확률", card.Chance, v => { card.Chance = Mathf.Clamp01(v); MarkDirty(card); }));
            row.Add(NumberField("지속", card.Duration, v => { card.Duration = Mathf.Max(0f, v); MarkDirty(card); }));

            var stack = new IntegerField();
            stack.value = card.StackLimit;
            stack.style.width = 34f;
            stack.tooltip = "중첩 상한";
            stack.RegisterValueChangedCallback(evt =>
            {
                card.StackLimit = Mathf.Max(1, evt.newValue);
                MarkDirty(card);
            });
            row.Add(stack);

            return row;
        }

        VisualElement NumberField(string tooltip, float value, System.Action<float> onChanged)
        {
            var field = new FloatField();
            field.value = value;
            field.style.width = 58f;
            field.style.marginLeft = 3f;
            field.tooltip = tooltip;
            field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));

            return field;
        }

        // ---------- 데이터 에셋 (Data 하위 전체) ----------

        VisualElement BuildAssetSection()
        {
            var section = Shell("데이터 에셋 (Assets/RushGame/Data)", out var body);

            body.Add(Note("타워/몬스터/스테이지/난이도/보상 전부. 접힌 항목을 펼치면 인스펙터와 같은 항목을 그대로 편집한다"));

            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.marginBottom = 6f;

            _assetSearch = new TextField();
            _assetSearch.style.flexGrow = 1f;
            _assetSearch.RegisterValueChangedCallback(evt => RebuildAssetList());
            filterRow.Add(_assetSearch);

            var refreshButton = new Button(RebuildAssetList);
            refreshButton.text = "새로고침";
            refreshButton.style.width = 80f;
            refreshButton.style.marginLeft = 6f;
            filterRow.Add(refreshButton);

            body.Add(filterRow);

            _assetSummary = Note("");
            body.Add(_assetSummary);

            _assetList = new VisualElement();
            body.Add(_assetList);

            return section;
        }

        void RebuildAssetList()
        {
            if (_assetList == null)
                return;

            _assetList.Clear();

            string search = _assetSearch != null ? _assetSearch.value : "";

            var groups = new SortedDictionary<string, List<AssetEntry>>(System.StringComparer.OrdinalIgnoreCase);
            var guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { DataRoot });

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                if (asset == null)
                    continue;

                if (!PassesAssetFilter(asset, search))
                    continue;

                string group = GroupNameOf(path);

                if (!groups.TryGetValue(group, out var list))
                {
                    list = new List<AssetEntry>();
                    groups.Add(group, list);
                }

                list.Add(new AssetEntry(guid, asset));
            }

            int total = 0;

            foreach (var pair in groups)
            {
                pair.Value.Sort((a, b) => string.CompareOrdinal(a.Asset.name, b.Asset.name));

                _assetList.Add(BuildAssetGroup(pair.Key, pair.Value));
                total += pair.Value.Count;
            }

            if (total == 0)
                _assetList.Add(Note("조건에 맞는 에셋이 없음 - Stage Command Center에서 데이터 생성 실행"));

            if (_assetSummary != null)
                _assetSummary.text = $"{total}개 에셋 / {groups.Count}개 폴더";
        }

        /// <summary>Data 루트 기준 첫 번째 하위 폴더 이름. 루트에 바로 놓인 에셋은 별도 그룹으로 묶는다.</summary>
        static string GroupNameOf(string assetPath)
        {
            string prefix = DataRoot + "/";

            if (!assetPath.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return "(그 외)";

            string relative = assetPath.Substring(prefix.Length);
            int slash = relative.IndexOf('/');

            if (slash < 0)
                return "(루트)";

            return relative.Substring(0, slash);
        }

        static bool PassesAssetFilter(ScriptableObject asset, string search)
        {
            if (string.IsNullOrEmpty(search))
                return true;

            if (asset.name.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (asset.GetType().Name.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>목록 항목 하나. 펼치는 시점에 다시 읽을 수 있도록 GUID를 함께 든다.</summary>
        readonly struct AssetEntry
        {
            public readonly string Guid;
            public readonly ScriptableObject Asset;

            public AssetEntry(string guid, ScriptableObject asset)
            {
                Guid = guid;
                Asset = asset;
            }
        }

        VisualElement BuildAssetGroup(string groupName, List<AssetEntry> assets)
        {
            var foldout = new Foldout();
            foldout.text = $"{groupName} ({assets.Count})";
            foldout.value = !_collapsedGroups.Contains(groupName);
            foldout.style.marginBottom = 4f;
            foldout.RegisterValueChangedCallback(evt =>
            {
                // 하위 폴드아웃 이벤트가 올라오므로 자기 자신의 변경만 기록한다
                if (evt.target != foldout)
                    return;

                if (evt.newValue)
                {
                    _collapsedGroups.Remove(groupName);
                    return;
                }

                _collapsedGroups.Add(groupName);
            });

            foreach (var entry in assets)
                foldout.Add(BuildAssetRow(entry));

            return foldout;
        }

        VisualElement BuildAssetRow(AssetEntry entry)
        {
            var foldout = new Foldout();
            foldout.text = $"{entry.Asset.name}  ({entry.Asset.GetType().Name})";
            foldout.value = false;
            foldout.style.marginLeft = 10f;

            // 스테이지처럼 무거운 에셋이 있어 인스펙터는 처음 펼칠 때 한 번만 만든다
            bool inspectorBuilt = false;

            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != foldout)
                    return;

                if (!evt.newValue)
                    return;

                if (inspectorBuilt)
                    return;

                inspectorBuilt = true;
                foldout.Add(BuildAssetInspector(entry.Guid));
            });

            return foldout;
        }

        VisualElement BuildAssetInspector(string guid)
        {
            var container = new VisualElement();

            // 목록을 만든 뒤 삭제/재임포트됐을 수 있어 펼치는 시점에 다시 읽는다
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));

            if (asset == null)
            {
                container.Add(Note("에셋을 찾을 수 없음 - 새로고침 필요"));
                return container;
            }

            var pingButton = new Button(() =>
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            });
            pingButton.text = "프로젝트에서 선택";
            pingButton.style.alignSelf = Align.FlexStart;
            container.Add(pingButton);

            var serialized = new SerializedObject(asset);

            var inspector = new InspectorElement(serialized);
            inspector.style.marginBottom = 6f;
            container.Add(inspector);

            // 편집 즉시 저장 / 플레이 중이면 종료 시점 저장 예약. 카드 편집과 동일한 규칙
            container.TrackSerializedObjectValue(serialized, _ => MarkDirty(asset));

            return container;
        }

        // ---------- 공용 ----------

        static VisualElement Shell(string title, out VisualElement body)
        {
            var shell = new VisualElement();
            shell.style.backgroundColor = panelBackground;
            shell.style.marginBottom = 10f;
            shell.style.borderLeftWidth = 1f;
            shell.style.borderRightWidth = 1f;
            shell.style.borderTopWidth = 1f;
            shell.style.borderBottomWidth = 1f;
            shell.style.borderLeftColor = panelBorderColor;
            shell.style.borderRightColor = panelBorderColor;
            shell.style.borderTopColor = panelBorderColor;
            shell.style.borderBottomColor = panelBorderColor;

            var titleLabel = new Label(title);
            titleLabel.style.color = Color.white;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.fontSize = 12;
            titleLabel.style.paddingLeft = 12f;
            titleLabel.style.paddingTop = 10f;
            titleLabel.style.paddingBottom = 8f;
            shell.Add(titleLabel);

            body = new VisualElement();
            body.style.paddingLeft = 12f;
            body.style.paddingRight = 12f;
            body.style.paddingBottom = 12f;
            shell.Add(body);

            return shell;
        }

        static Label Note(string text)
        {
            var label = new Label(text);
            label.style.color = subtleTextColor;
            label.style.fontSize = 11;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 4f;
            label.style.marginBottom = 4f;

            return label;
        }

        VisualElement IntRow(string title, int value, System.Action<int> onChanged)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2f;

            var label = new Label(title);
            label.style.width = 120f;
            label.style.fontSize = 11;
            label.style.color = subtleTextColor;
            row.Add(label);

            var field = new IntegerField();
            field.value = value;
            field.style.flexGrow = 1f;
            field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            row.Add(field);

            return row;
        }
    }
}
