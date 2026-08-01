---
name: code-style
description: The user's preferred source-code style. Apply WHENEVER writing or editing code (C#/Unity is primary, but the principles are language-agnostic). Favors explicit guard clauses with early returns, blank lines separating logical steps, and readable expanded blocks over compact ternaries, expression-bodied logic, or computed single-return one-liners. Use proactively on any code authoring, refactoring, or formatting task — do not wait to be asked.
---

# 코드 스타일 (사용자 선호)

작성·수정하는 모든 소스 코드에 적용한다. 핵심 가치: **"똑똑한 압축"보다 읽으면서 흐름이 그대로 따라오는 가독성.** 한 메서드를 위에서 아래로 읽으면 분기와 출구가 또렷하게 보여야 한다.

## 원칙

1. **가드절 + 이른 리턴(early return)**
   - 조건을 만족/불만족하면 즉시 `return`. `else` 사다리·깊은 중첩 지양, 평탄하게.
   - `return condition;` 식으로 압축하지 말고, 조건마다 블록을 잡고 명시적으로 `return true;` / `return false;`.

2. **단계별 수직 공백**
   - 논리 단계(검증 → 처리 → 폴백) 사이에 **빈 줄**을 넣어 "한 호흡씩" 읽히게 한다.

3. **압축/삼항 지양**
   - `cond ? a : b`, `bool x = ...; return x;`, 식 본문 멤버(`=> expr`)에 **분기·로직을 욱여넣지 않는다.**
   - 단순 위임/순수 접근자 한 줄(`=> _value;`)은 허용. 분기·부작용이 있으면 풀어쓴다.

4. **주석은 그 블록 바로 위에** 둔다(줄 끝 꼬리 주석보다 선호).

## 예시 (Before → After)

### Before — 압축형 (지양)
```csharp
static bool LandedQuick()
{
    if (EditorPrefs.GetBool(LandedPrefKey, false)) return true;
    bool landed = LandedFsCheck();
    if (landed) EditorPrefs.SetBool(LandedPrefKey, true);
    return landed;
}
```

### After — 선호 스타일
```csharp
static bool LandedQuick()
{
    // 캐시된 양성값 신뢰(빠른 경로, 파일시스템 미접근)
    if (EditorPrefs.GetBool(LandedPrefKey, false))
        return true;

    if (LandedFsCheck())
    {
        EditorPrefs.SetBool(LandedPrefKey, true);
        return true;
    }

    return false;
}
```

다른 흔한 변환:
- `else if` 체인 → 각 케이스를 가드절로 처리하고 이른 리턴.
- `Outcome oc = cond ? A : B;` → `if (cond) { ... } else { ... }` 또는 가드절로 분리.
- 분기 있는 식 본문 멤버 → 일반 메서드 본문으로 풀어쓰기.

## 적용 체크리스트
- [ ] `else` 사다리 대신 가드절로 평탄화했는가?
- [ ] 각 논리 단계가 빈 줄로 분리됐는가?
- [ ] 분기/부작용을 삼항·식본문으로 압축하지 않았는가?
- [ ] 메서드의 출구(`return`)들이 명시적으로 드러나는가?
- [ ] 주석이 설명 대상 블록 바로 위에 있는가?

## 비고
- 기존 파일을 수정할 때는, 주변 코드가 이미 다른 스타일이면 **새로/변경하는 코드부터** 이 스타일로 맞춘다(대규모 무관 리포맷은 별도 요청 시에만).
- 언어 문법상 자연스러운 관용구(LINQ 한 줄, `using` 선언 등)는 그대로 허용 — 규칙은 "분기 로직의 가독성"에 관한 것이다.
