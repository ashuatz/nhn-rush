# CLAUDE.md

이 레포는 48시간 안에 게임을 제작하는 해커톤 테스트를 위한 사전 레포지토리다.
Unity 프로젝트이며, 아래 규칙은 모든 작업자의 PC에서 동일하게 적용되어야 한다.

## 문서

- 기획서 등 문서는 `Docs/` 하위에 저장한다.
- 기획서 작성 시 `Docs/기획서_디자인가이드.md` 양식을 따른다.

## 대화 / 출력 규칙

- AskUserQuestion 도구는 되도록 사용하지 않는다. 합리적인 기본값을 선택하고 진행 후 보고한다.
- 주석, 파일 이름, 문서 등에 이모지(유니코드 특수문자)를 되도록 사용하지 않는다.

## 코드 작성 규칙

- 과대한 시스템 구현을 자제한다. 해커톤 규모에 맞게 핵심 동작 위주로 작성한다.
- 기존 코드에 대한 최소 침투(minimal intrusion) 방식으로 핵심 동작을 구현한다.
- 런타임 오류 트래킹이 수월하도록 대시보드 혹은 로그 구성을 고려한다.
- 코드 스타일은 `/code-style` 스킬을 참조한다. 사용자 스타일 요청이 있으면 해당 스킬을 갱신한다.
- 구현 전에 구현 계획을 사람이 읽기 쉽게 간단히 공유한다.
- C# 작성/수정 후 `/unity-cs-compile-check` 스킬로 dotnet 컴파일을 검증한다.
- 작성 완료 후 `/codex` 스킬로 검토를 진행한다. (모델: gpt-5.6-sol, reasoning: xhigh)

## 엔진(Unity) 기능 구현 규칙

- 부트스트랩 등 최초 1회 런타임 오브젝트 생성 방식(RuntimeInitializeOnLoad로 매니저 생성 등)을 회피한다.
- 베이크할 수 있는 정보는 에디트 타임에 베이크한다.
- 에디트 모드에서 룩(look) 등을 확인할 수 있는 기능이라면 에디터 윈도우를 만들어 셋업할 수 있게 구성한다.

## 스킬 위치

- 프로젝트 공용 스킬은 `.claude/skills/` 에 커밋되어 있다. 레포를 클론하면 동일하게 사용 가능하다.
  - `code-style`: 코드 스타일 규칙
  - `unity-cs-compile-check`: asmdef 기반 csproj 매핑 후 dotnet 컴파일 검증
  - `codex`: Codex CLI 위임 검토 (별도로 Codex CLI 설치 필요)
  - `unity-mcp-skill`, `unity-editor-tool-gotchas`, `Unity-Editor-Layout-SKILL`: Unity 에디터 도구 제작 시 참조
