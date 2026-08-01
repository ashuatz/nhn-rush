---
name: unity-editor-tool-gotchas
description: Unity 에디터 윈도우/도구 제작 시 반드시 확인할 함정 모음 (UI Toolkit TreeView 상태 보존, 자기유발 hierarchyChanged 차단, SerializedObject 프록시 소독, GlobalObjectId 플레이 모드 함정, 도구 상태 저장 위치/생명주기). EditorWindow, TreeView/ListView, SerializedObject 복사/비교, 스냅샷·diff류 도구, 오브젝트 영속 참조를 다루는 작업이면 코드를 쓰기 전에 이 스킬을 먼저 읽을 것. 2026-07 ComponentSnapshot 재구성에서 실제로 터진 버그들의 오답노트.
---

# Unity 에디터 도구 제작 함정 모음

ComponentSnapshot 도구 제작(2026-07)에서 실제로 터진 버그 기반. 각 항목은 "증상 → 원인 → 처방" 구조.

## 1. UI Toolkit TreeView — 상태 보존

### 증상: 리빌드마다 접힘 상태/스크롤이 초기화된다
- **원인 1**: 아이템 ID를 리빌드마다 순번(`nextId++`)으로 재발급 → TreeView가 다른 노드로 인식.
- **원인 2**: `SetRootItems()` 자체가 확장 상태를 리셋한다 (Unity 6000.3 확인).
- **처방**:
  ```csharp
  // (a) 콘텐츠 키 기반 안정 ID — 같은 그룹/엔트리는 리빌드 후에도 같은 ID
  Dictionary<string, int> _idByKey; int GetStableId(string key) { ... }

  // (b) 리빌드 전 확장 스냅샷 → 리빌드 후 복원
  var expanded = viewController.GetAllItemIds().Where(id => tree.IsExpanded(id)).ToList();
  tree.SetRootItems(items); tree.Rebuild();
  var valid = new HashSet<int>(tree.viewController.GetAllItemIds());
  foreach (var id in expanded) if (valid.Contains(id)) tree.ExpandItem(id, false, false);
  tree.RefreshItems();
  ```
- `ExpandAll()`은 소스 전환/검색어 변경 시에만. 매 리빌드 호출 금지.

### 그 외 TreeView/ListView 규칙
- 콜백은 **makeItem에서 1회 등록**, bindItem에서는 `userData`만 갈아끼운다. bindItem에서 `RegisterValueChangedCallback`을 부르면 재바인딩마다 핸들러가 쌓인다. Button은 `button.clickable = new Clickable(...)` 대입으로 교체 가능.
- 더블/트리플 클릭: `RegisterCallback<ClickEvent>(evt => evt.clickCount == 2 / 3)`.
- 루트→리프가 자식 1개 체인이면 트리 데이터 빌드 단계에서 `"A / B / C"` 로 병합 (매번 펼치는 불편 제거).

## 2. 자기유발 이벤트 루프 차단

### 증상: 항목만 선택했는데 트리가 통째로 리빌드됨
- **원인**: 도구가 비교/캡처용 **숨김 프록시 GameObject를 생성/파괴** → `EditorApplication.hierarchyChanged` 발화 → 도구 자신의 refresh 콜백 → UI 상태 소실.
- **처방**: 프록시를 만드는 작업 전후에 시간 기반 mute.
  ```csharp
  double _muteUntil;
  void MuteHierarchyEvents() => _muteUntil = EditorApplication.timeSinceStartup + 0.15;
  void OnHierarchyChanged() { if (EditorApplication.timeSinceStartup < _muteUntil) return; ... }
  ```
  이벤트가 메서드 리턴 후 도착할 수 있으므로 작업 시작+종료 양쪽에서 호출.

## 3. SerializedObject 복사/비교 — 프록시 소독 패턴

- **라이브 컴포넌트에 `EditorJsonUtility.FromJsonOverwrite` 금지.** JSON에 `m_Father`/`m_Children`/프리팹 메타가 instanceID로 섞여 계층이 손상될 수 있다. 대신:
  1. 캡처: `HideAndDontSave` 프록시에 visible 프로퍼티만 복사 → 프록시를 `ToJson` (소독).
  2. 적용: JSON → 프록시 복원 → 프록시에서 대상으로 **visible 최상위 프로퍼티 복사**.
- **`m_Enabled`는 visible 순회(NextVisible)에 안 잡힌다.** 캡처/비교/적용 모두 `FindProperty("m_Enabled")` 명시 처리.
- 두 SerializedObject 병렬 순회(`NextVisible` 동시 전진)는 타입이 어긋나면 쌍이 밀린다 → **propertyPath 기준 `FindProperty` 매칭**.
- `SerializedProperty.DataEquals`는 정확 비교라 **부동소수점 노이즈**를 diff로 잡는다 → float 성분 허용 오차(절대/상대 1e-5) 비교 레이어 필요. Quaternion은 q ≡ -q.
- RectTransform 프록시는 `dummy.transform`(Transform)이 아니라 `AddComponent<RectTransform>()`.
- 대량 캡처/비교 시 프록시는 **타입별 풀로 재사용** (visible 전체 복사가 덮어쓰므로 재사용 안전). GameObject 생성/파괴 비용 제거.

## 4. 오브젝트 영속 식별자 (GlobalObjectId)

- 씬 오브젝트 GID = 씬 GUID + 씬 파일 fileID → 세션/리로드를 견딤. instanceID 키는 에디터 재시작 시 전멸.
- **함정: 프리팹 인스턴스는 플레이 모드 로드 시 언팩되어 GID가 달라진다.** 플레이 중 캡처가 필요하면 ExitingEditMode 시점에 `instanceID → 에디트 GID` 매핑을 배치 API(`GetGlobalObjectIdsSlow`)로 만들어 SessionState에 보관.
- 씬 오브젝트의 **instanceID는 동일 에디터 세션의 플레이 전환에서 유지**된다 (Selection 유지와 같은 메커니즘) — 매핑 키로 신뢰 가능.
- 런타임 Instantiate 오브젝트는 GID 무효 — 폴백 계층(씬 경로 + 시블링 인덱스 + **전 깊이 이름 경로**) 필요. 이름 검증 없이 인덱스만 쓰면 동명 반복 구조에서 오적용.
- 참조 유실 대비: 캡처 시 참조 대상의 서술 시그니처(이름/계층/트랜스폼/컴포넌트 구성)를 기록해 두면 fuzzy 재탐색 + "(유실: 이름)" 표시가 가능.
- `GetGlobalObjectIdSlow`를 루프에서 개별 호출 금지 — 배치 API 사용.

## 5. 도구 상태 저장 위치와 생명주기

- 개인 작업 데이터(스냅샷, 히스토리 등)는 **프로젝트 `UserSettings/` + `ScriptableSingleton<T>` + `[FilePath(..., Location.ProjectFolder)]`**. 패키지 내 Resources 에셋으로 저장하면 공유 저장소에 커밋 후보로 계속 올라온다.
- ScriptableSingleton은 도메인 리로드 시 파일에서 재로드 — **mutate 즉시 `Save(true)`** 안 하면 유실.
- **임시 상태(프리뷰 등)는 창 생명주기에 묶어라.** 영속 플래그만 두면 창을 새로 열었을 때 유령 상태가 뜬다(OnEnable에서 잔여 상태 감지→정리, OnDisable에서 세션 종료).
- `EditorApplication.playModeStateChanged`에서 인스턴스 캐시 무효화 (Entered EditMode/PlayMode).

## 6. 검증 워크플로우

- `dotnet build` 검증 시 Unity가 csproj를 재생성해 새 파일이 빠져 있을 수 있다 → csproj 사본에 해당 폴더의 Compile 항목을 **전부 제거 후 현재 파일 목록으로 재주입**해서 빌드.
- 새 .cs 파일의 .meta: Unity가 열려 있으면 자동 생성되지만, 커밋 전 존재 확인. 폴더 메타 포함.
- 참조용 외부 에셋(컴파일 불가/불필요)은 폴더명 `~` 접미사로 Unity 임포트에서 제외.
- 상용 에셋 원본은 커밋 금지 — .gitignore 등록.

## 체크리스트 (새 에디터 도구 시작 시)

- [ ] 트리/리스트 ID가 콘텐츠 키 기반인가? 리빌드 후 접힘/선택이 보존되는가?
- [ ] 도구가 만드는 씬 부작용(프록시 등)이 자기 refresh를 유발하지 않는가?
- [ ] 라이브 객체에 FromJsonOverwrite 하고 있지 않은가? m_Enabled 처리했는가?
- [ ] 오브젝트 참조가 세션/플레이 전환을 견디는가? (instanceID 단독 사용 금지)
- [ ] 상태 파일이 UserSettings에 있는가? 임시 상태가 창 생명주기에 묶여 있는가?
- [ ] float 비교에 허용 오차가 있는가?
