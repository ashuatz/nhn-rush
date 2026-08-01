---
name: md-pdf
description: Markdown 파일을 스타일링된 PDF 보고서로 변환하는 스킬. Pandoc + WeasyPrint 엔진, CJK(한국어) 폰트, Mermaid 다이어그램, 표지 페이지(frontmatter), 자동 목차, 페이지 번호를 지원한다. 보고서 작성 후 PDF 변환, 리서치/분석 결과 PDF화, 프로젝트 구조 분석 보고서 PDF 생성 등 모든 보고서·PDF 관련 작업에 이 스킬을 사용한다. "PDF로 만들어", "보고서 PDF", "리포트 생성", "PDF 변환" 등의 요청 시 반드시 이 스킬을 사용할 것.
---

# md-pdf

리서치·분석 결과 Markdown 파일을 스타일링된 A4 PDF 보고서로 변환한다.

엔진: **Pandoc + WeasyPrint**

## Quick Start

```bash
python3 scripts/md_to_pdf.py \
  --input ./report.md \
  --output ./report.pdf
```

Options:
- `--css <path>` — 커스텀 CSS (기본: `assets/report-style.css`)
- `--template <path>` — Pandoc HTML 템플릿 (기본: `assets/report-template.html`)
- `--work-dir <path>` — Mermaid 이미지 임시 디렉토리 (기본: 자동)
- `--no-toc` — 목차 비활성화 (기본: 자동 생성)
- `--toc-depth <n>` — 목차 깊이 (기본: 3)
- `--number-sections` — 섹션 번호 자동 추가
- `--keep-temp` — 변환 후 중간 파일 유지

## Frontmatter → 표지 페이지

Markdown 파일 상단에 YAML frontmatter를 추가하면 표지 페이지가 자동 생성된다:

```yaml
---
title: 보고서 제목
subtitle: 부제목
date: 2026-03-17
author: 작성자
abstract: 보고서 개요
keywords: 키워드1, 키워드2
---
```

`title`이 있으면 표지가 생성되고, 나머지 필드는 선택사항이다.
Pandoc이 frontmatter를 직접 파싱하여 템플릿에 전달한다.

## 출처 목록 포맷터

출처 JSON 배열을 정렬된 Markdown으로 변환한다:

```bash
python3 scripts/source_formatter.py \
  --input ./sources.json \
  --output ./sources.md
```

입력 형식:
```json
[
  {"title": "제목", "url": "https://...", "date": "2026-03-01", "type": "official"}
]
```

## Mermaid 렌더러 (단독 사용 시)

```bash
python3 scripts/mermaid_render.py \
  --input ./diagram.mmd \
  --output ./diagram.png
```

또는 코드 직접 입력:
```bash
python3 scripts/mermaid_render.py \
  --code 'graph TD; A[원인] --> B[결과];' \
  --output ./diagram.svg
```

## Environment Setup

의존성 설치는 [references/setup.md](references/setup.md) 참조.

**최소 요구사항:** Python 3.10+, `pandoc`, `weasyprint`, `markdown`, Noto Sans CJK KR font.

## How It Works

1. YAML frontmatter → Pandoc이 자동 파싱 → 템플릿으로 표지 페이지 생성
2. ` ```mermaid ` 블록 감지 → `scripts/mermaid_render.py`로 PNG 렌더링 (`mmdc` 필요)
3. Pandoc `--pdf-engine=weasyprint` 로 Markdown → HTML → PDF 변환
4. `assets/report-template.html` — 표지/목차 레이아웃
5. `assets/report-style.css` — CJK 폰트, A4 페이지, 헤더/푸터, 표 스타일

## Notes

- Pandoc이 PATH에 없으면 실행되지 않는다.
- Mermaid 렌더링은 `mmdc`가 PATH에 있어야 한다. Mermaid 블록이 없으면 불필요.
- 한국어 텍스트는 Noto Sans CJK KR 폰트 필요. 없으면 글자가 깨짐.
- 출력: A4, 자동 목차, 페이지 번호·섹션 헤더 포함.
