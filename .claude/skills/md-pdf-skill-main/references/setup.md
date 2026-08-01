# Environment Setup

## Requirements

- Python 3.10+
- WeasyPrint
- markdown (Python package)
- **pandoc** — Markdown → PDF 변환 엔진
- mmdc (Mermaid CLI) — Markdown에 Mermaid 다이어그램이 포함된 경우만 필요
- Noto Sans CJK KR font — 한국어/CJK 텍스트 렌더링에 필수

## Install

### macOS

```bash
# Pandoc
brew install pandoc

# Python 패키지
pip install weasyprint markdown

# CJK 폰트
brew install --cask font-noto-sans-cjk-kr

# Mermaid CLI (선택 — Mermaid 블록 사용 시)
npm install -g @mermaid-js/mermaid-cli
```

### Linux (Ubuntu/Debian)

```bash
# Pandoc
sudo apt-get install pandoc

# Python 패키지
pip install weasyprint markdown

# CJK 폰트
sudo apt-get install fonts-noto-cjk

# Mermaid CLI (선택)
npm install -g @mermaid-js/mermaid-cli
```

### Windows

```powershell
# Pandoc
winget install JohnMacFarlane.Pandoc

# Python 패키지
pip install weasyprint markdown

# CJK 폰트
winget install Google.NotoSansCJK

# GTK3 런타임 (WeasyPrint 의존성)
# https://github.com/nickvdyck/weasyprint-win/releases

# Mermaid CLI (선택)
npm install -g @mermaid-js/mermaid-cli
```

## Verify

```bash
# Pandoc 확인
pandoc --version

# WeasyPrint 확인
python3 -c "import weasyprint; print('ok')"

# 폰트 확인 (Linux/macOS)
fc-list | grep -i "Noto Sans CJK"

# mmdc 확인 (선택)
mmdc --version
```

## Notes

- Pandoc이 없으면 변환이 실행되지 않는다. 반드시 설치 후 PATH에 있어야 한다.
- CJK 폰트 미설치 시 한국어/중국어 문자가 깨져 출력된다.
- Mermaid 렌더링은 `mmdc`가 PATH에 있어야 한다. 없으면 Mermaid 블록에서 에러 발생.
- Windows에서 WeasyPrint는 GTK3 런타임이 필요하다. 설치 후 PATH에 GTK3 bin 디렉토리 추가 필요.
