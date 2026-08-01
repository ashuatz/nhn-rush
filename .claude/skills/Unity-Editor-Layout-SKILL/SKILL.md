---
name: Unity-Editor-Layout-SKILL
description: 에디터 윈도우를 만들 때 사용하는 UI 디자인 규칙입니다.
---

# Insert instructions below

## 📌 1. 색상 규칙

```csharp
// 항상 맨 위에 색상을 먼저 정의하세요
private static readonly Color windowBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
private static readonly Color panelBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
private static readonly Color panelBorderColor = new Color(0.17f, 0.17f, 0.17f, 1f);
private static readonly Color headerBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
private static readonly Color accentColor = new Color(0.36f, 0.36f, 0.36f, 1f);
private static readonly Color subtleTextColor = new Color(0.78f, 0.78f, 0.78f, 1f);
```

### 색상 사용처

| 색상 | 용도 |
|------|------|
| `windowBackground` | 창 전체 배경 |
| `panelBackground` | 패널 안쪽 배경 |
| `panelBorderColor` | 테두리선 |
| `headerBackground` | 헤더 배경 |
| `accentColor` | 강조 막대 |
| `subtleTextColor` | 일반 텍스트 |
| `Color.white` | 중요한 텍스트 |

---

## 📦 2. 섹션 만들기

### 기본 구조

```
┌─────────────────────────────┐
│  헤더 (제목 + 뱃지)          │ ← CreateSectionHeader()
├─────────────────────────────┤
│  얇은 강조선                 │ ← CreateSectionAccentBar()
├─────────────────────────────┤
│                             │
│  내용 영역 (body)            │ ← bodyContainer
│                             │
└─────────────────────────────┘
```

### 사용 패턴

```csharp
// 1단계: 섹션 틀 만들기
VisualElement body;
var section = CreateSectionShell("제목", "뱃지", out body);

// 2단계: body에 내용 추가
body.Add(새로운요소);

// 3단계: 화면에 추가
rootContainer.Add(section);
```

---

## 🏗️ 3. CreateSectionShell 만들기

```csharp
private VisualElement CreateSectionShell(string title, string badge, 
    out VisualElement bodyContainer)
{
    var shell = new VisualElement();
    
    // 기본 스타일
    shell.style.flexDirection = FlexDirection.Column;
    shell.style.backgroundColor = panelBackground;
    shell.style.overflow = Overflow.Hidden;
    shell.style.marginBottom = 10f;
    
    // 테두리 (항상 1�셀로 통일)
    shell.style.borderLeftWidth = 1f;
    shell.style.borderRightWidth = 1f;
    shell.style.borderTopWidth = 1f;
    shell.style.borderBottomWidth = 1f;
    shell.style.borderLeftColor = panelBorderColor;
    shell.style.borderRightColor = panelBorderColor;
    shell.style.borderTopColor = panelBorderColor;
    shell.style.borderBottomColor = panelBorderColor;
    
    // 내용물 추가
    shell.Add(CreateSectionHeader(title, badge));
    shell.Add(CreateSectionAccentBar());
    
    // body 영역
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
```

---

## 📋 4. 헤더 만들기

### 헤더 구조

```
[●●] 제목          [뱃지]
 ↑   ↑             ↑
강조선 제목         오른쪽 뱃지
```

### 코드

```csharp
private VisualElement CreateSectionHeader(string title, string badgeText)
{
    var header = new VisualElement();
    header.style.height = 40f;
    header.style.flexDirection = FlexDirection.Row;
    header.style.alignItems = Align.Center;
    header.style.justifyContent = Justify.SpaceBetween;
    header.style.backgroundColor = headerBackground;
    header.style.paddingLeft = 14f;
    header.style.paddingRight = 14f;
    
    // 왼쪽 그룹 (강조선 + 제목)
    var leftGroup = new VisualElement();
    leftGroup.style.flexDirection = FlexDirection.Row;
    leftGroup.style.alignItems = Align.Center;
    
    // 강조선
    var accent = new VisualElement();
    accent.style.width = 2f;
    accent.style.height = 18f;
    accent.style.backgroundColor = accentColor;
    accent.style.marginRight = 8f;
    leftGroup.Add(accent);
    
    // 제목
    var titleLabel = new Label(title);
    titleLabel.style.color = Color.white;
    titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
    titleLabel.style.fontSize = 12;
    leftGroup.Add(titleLabel);
    
    header.Add(leftGroup);
    
    // 뱃지 (옵션)
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
```

### 강조선 만들기

```csharp
private VisualElement CreateSectionAccentBar()
{
    var bar = new VisualElement();
    bar.style.height = 1f;
    bar.style.backgroundColor = panelBorderColor;
    return bar;
}
```

---

## 📂 5. Foldout 꾸미기

```csharp
private void StyleFoldout(Foldout foldout, string badgeText)
{
    if (foldout == null) return;
    
    // 테두리
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
    
    // 토글 부분 (접기/펼치기 버튼)
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
        
        // 뱃지 추가 (옵션)
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
    
    // 내용 영역
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
```

---

## 📏 6. 여백 규칙

### 패딩 (안쪽 여백)

| 요소 | 좌우 | 위 | 아래 |
|------|------|-----|------|
| 섹션 body | 14f | 12f | 14f |
| Foldout 내용 | 12f | 10f | 12f |
| 헤더 | 14f | - | - |
| 강조 박스 | 12 | 10 | 10 |

### 마진 (바깥 여백)

| 요소 | 아래 여백 |
|------|----------|
| 섹션 | 10f |
| Foldout | 8f |
| 작은 요소 | 4f ~ 6f |

---

## 🎯 7. 입력 필드 배치

### 한 줄 레이아웃

```csharp
var row = new VisualElement();
row.style.flexDirection = FlexDirection.Row;
row.style.alignItems = Align.Center;

// 라벨 (고정 너비)
var label = new Label("설정 이름");
label.style.width = 120;
label.style.flexShrink = 0;
row.Add(label);

// 입력 필드 (남은 공간 채우기)
var field = new TextField();
field.style.flexGrow = 1;
field.style.width = 180;
field.style.minWidth = StyleKeyword.Auto;
field.style.maxWidth = StyleKeyword.Auto;
row.Add(field);
```

---

## 🔘 8. 토글 + 입력 패턴

토글을 켜면 입력 필드가 활성화되는 패턴입니다.

```csharp
var toggle = new Toggle("사용하기");
var field = new TextField();

// 초기 상태 설정
field.SetEnabled(toggle.value);

// 토글 변경 감지
toggle.RegisterValueChangedCallback(evt =>
{
    field.SetEnabled(evt.newValue);
});
```

---

## 🎨 9. 강조 박스 만들기

특정 옵션을 눈에 띄게 만들 때 사용합니다.

```csharp
var box = new VisualElement();
box.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
box.style.paddingLeft = 12;
box.style.paddingRight = 12;
box.style.paddingTop = 10;
box.style.paddingBottom = 10;
box.style.borderLeftWidth = 2;
box.style.borderLeftColor = accentColor;  // 왼쪽 색깔 막대
box.style.marginBottom = 12;
```

### 다양한 강조 색상

```csharp
// 회색 (일반)
borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 1f);

// 파란색 (정보)
borderLeftColor = new Color(0.4f, 0.6f, 1f, 1f);

// 주황색 (주의)
borderLeftColor = new Color(1f, 0.6f, 0.2f, 1f);

// 빨간색 (경고)
borderLeftColor = new Color(1f, 0.3f, 0.3f, 1f);
```

---

## ⚠️ 10. 주의사항

### ❌ 하지 말 것

- 테두리 너비를 다르게 설정 (항상 `1f`)
- 색상을 직접 코드에 입력 (변수 사용)
- 여백 없이 요소 붙이기
- 일관성 없는 폰트 크기 사용

### ✅ 꼭 할 것

- 색상 변수 재사용
- 일관된 여백 사용 (14f, 12f, 10f)
- 섹션 단위로 묶기
- 테두리 1픽셀 통일

---

## 📋 빠른 체크리스트

새 UI를 만들 때 확인하세요:

- [ ] 색상 변수 정의했나?
- [ ] `CreateSectionShell` 사용했나?
- [ ] 패딩 14f/12f 적용했나?
- [ ] 테두리 1f로 통일했나?
- [ ] Foldout에 `StyleFoldout` 적용했나?
- [ ] 입력 필드에 `flexGrow = 1` 설정했나?
- [ ] 라벨에 고정 너비 설정했나?

---

## 🎓 예시: 완전한 섹션 만들기

```csharp
// 1. 섹션 생성
VisualElement body;
var section = CreateSectionShell("설정", "OPTIONS", out body);

// 2. Foldout 추가
var foldout = new Foldout { text = "상세 옵션", value = true };
StyleFoldout(foldout, "ADVANCED");

// 3. 입력 필드 추가
var row = new VisualElement();
row.style.flexDirection = FlexDirection.Row;
row.style.alignItems = Align.Center;

var label = new Label("값");
label.style.width = 120;
label.style.flexShrink = 0;
row.Add(label);

var slider = new Slider("범위", 0f, 10f);
slider.style.flexGrow = 1;
slider.showInputField = true;
row.Add(slider);

foldout.Add(row);
body.Add(foldout);

// 4. 화면에 추가
rootContainer.Add(section);
```

---

## 📚 참고 자료

- 폰트 크기: 헤더 `12`, 일반 `11`, 작은 텍스트 `10`, 뱃지 `9`
- 높이: 헤더 `40f`, Foldout 토글 `26f`
- 테두리: 항상 `1f`
- 강조선: 너비 `2f`, 높이 `18f`