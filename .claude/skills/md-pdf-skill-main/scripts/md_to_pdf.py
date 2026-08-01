#!/usr/bin/env python3
"""
md-pdf: Pandoc + WeasyPrint 기반 Markdown → PDF 변환 스크립트.

파이프라인:
  1. Mermaid 코드블록 → PNG 사전 렌더링 (mmdc 필요; 없거나 실패하면 WARN 후 코드블록 유지)
  2. 선행 YAML frontmatter 분리 → --metadata-file (표지 변수)
  3. pandoc --pdf-engine=weasyprint 로 PDF 생성 (--toc 기본 on)
  4. 산출물 %PDF 무결성 검증

v0.17 렌더링 정합 패치 (upmirror-migrate 벤더본과 동일):
  • A2 (YAML 안전): 선행 frontmatter 만 분리해 --metadata-file 로 넘기고, 본문은
    --from=markdown+smart-yaml_metadata_block 로 렌더(pandoc markdown 은 yaml_metadata_block
    기본 활성이라 +smart 만으론 안 꺼짐 → 명시 subtract 필수, §34) → 본문 `---`(수평선/
    setext) 가 YAML 문서 구분자로 오인되어 파싱 실패하는 문제(§31) 제거.
  • A3 (무결성): 산출물 첫 바이트가 %PDF- 인지 검증 → 아니면 loud-fail(exit 1).
  • UTF-8 직독/직기 (CJK mojibake 방지).
"""
from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path

MERMAID_PATTERN = re.compile(r"```mermaid\s*(.*?)```", re.DOTALL | re.IGNORECASE)

# 선행 YAML frontmatter: 파일 맨 앞 `---` ~ `---`. 오프셋 0 앵커(re.match)라 본문 `---` 는 매치 안 됨.
FRONTMATTER_PATTERN = re.compile(r"^---\r?\n(.*?\r?\n)---[ \t]*\r?\n", re.DOTALL)


def _default_paths(script_file: Path) -> tuple[Path, Path, Path]:
    scripts_dir = script_file.parent
    skill_root = scripts_dir.parent
    css = skill_root / "assets" / "report-style.css"
    mermaid = scripts_dir / "mermaid_render.py"
    template = skill_root / "assets" / "report-template.html"
    return css, mermaid, template


def _split_frontmatter(md_text: str) -> tuple[str | None, str]:
    """A2: 선행 YAML frontmatter 를 본문에서 분리. (metadata_yaml, body) 반환.
    맨 앞 블록이고 내용이 YAML 같음(':' 포함)일 때만 frontmatter 로 취급."""
    match = FRONTMATTER_PATTERN.match(md_text)
    if not match:
        return None, md_text
    meta = match.group(1)
    if ":" not in meta:
        return None, md_text
    return meta, md_text[match.end():]


def _render_mermaids(md_text: str, work_dir: Path, mermaid_script: Path) -> str:
    """Mermaid 코드블록을 PNG로 사전 렌더링하고 이미지 참조로 대체한다."""
    if not MERMAID_PATTERN.search(md_text):
        return md_text

    work_dir.mkdir(parents=True, exist_ok=True)
    index = 0

    def _replace(match: re.Match[str]) -> str:
        nonlocal index
        index += 1
        code = match.group(1).strip()
        mmd_path = work_dir / f"diagram_{index}.mmd"
        img_path = work_dir / f"diagram_{index}.png"
        mmd_path.write_text(code + "\n", encoding="utf-8")

        proc = subprocess.run(
            [sys.executable, str(mermaid_script), "--input", str(mmd_path), "--output", str(img_path)],
            capture_output=True,
            text=True,
        )
        if proc.returncode != 0:
            raise RuntimeError(
                f"Mermaid #{index} 렌더링 실패:\n{proc.stderr.strip() or proc.stdout.strip()}"
            )
        return f"\n![Mermaid Diagram {index}]({img_path.as_posix()})\n"

    return MERMAID_PATTERN.sub(_replace, md_text)


def _run_pandoc(
    input_md: Path,
    output_pdf: Path,
    css_path: Path,
    template_path: Path | None,
    metadata_file: Path | None,
    toc: bool,
    number_sections: bool,
    toc_depth: int,
) -> None:
    """Pandoc + WeasyPrint 으로 PDF를 생성한다."""
    pandoc_bin = shutil.which("pandoc")
    if not pandoc_bin:
        raise RuntimeError("pandoc을 찾을 수 없습니다. 설치: brew install pandoc / choco install pandoc")

    cmd: list[str] = [
        pandoc_bin,
        str(input_md),
        "--pdf-engine=weasyprint",
        f"--css={css_path}",
        "--standalone",
        # A2: yaml_metadata_block 를 명시적으로 subtract. pandoc 의 `markdown` 은 이 확장을 기본 활성하므로
        # 맨 `markdown+smart` 만으론 본문 `---`(+뒤 `*`)가 mid-document YAML 로 잡혀 실패한다(§31/§34).
        # frontmatter 는 이미 분리되어 --metadata-file 로 전달되므로 reader 확장을 꺼도 표지는 정상.
        "--from=markdown+smart-yaml_metadata_block",
        "-o", str(output_pdf),
    ]
    if metadata_file is not None and metadata_file.exists():
        cmd += [f"--metadata-file={metadata_file}"]
    if template_path and template_path.exists():
        cmd += [f"--template={template_path}"]
    if toc:
        cmd += ["--toc", f"--toc-depth={toc_depth}"]
    if number_sections:
        cmd += ["--number-sections"]

    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"Pandoc 실패:\n{proc.stderr.strip()}")


def _verify_pdf(path: Path) -> None:
    """A3: 산출물이 %PDF- 시그니처로 시작하는지 확인. 아니면 비-PDF(HTML 등)이므로 loud-fail."""
    with open(path, "rb") as handle:
        head = handle.read(5)
    if head[:5] != b"%PDF-":
        raise RuntimeError(
            f"산출물이 PDF 가 아닙니다 — 첫 바이트 {head!r} (%PDF- 아님). 렌더러가 출력 확장자로 "
            f"writer 를 잘못 골라 HTML 을 냈을 가능성. 비-PDF 출하 거부. 원본 .md 는 보존됨."
        )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Markdown → PDF (Pandoc + WeasyPrint, CJK + Mermaid + 표지 지원)"
    )
    parser.add_argument("--input", type=Path, required=True, help="입력 Markdown 파일")
    parser.add_argument("--output", type=Path, required=True, help="출력 PDF 경로")
    parser.add_argument("--css", type=Path, default=None, help="커스텀 CSS 경로")
    parser.add_argument("--template", type=Path, default=None, help="Pandoc HTML 템플릿 경로")
    parser.add_argument("--work-dir", type=Path, default=None, help="Mermaid 임시 디렉토리")
    parser.add_argument("--toc", action=argparse.BooleanOptionalAction, default=True, help="목차 자동 생성 (기본 on)")
    parser.add_argument("--toc-depth", type=int, default=3, help="목차 깊이 (기본 3)")
    parser.add_argument("--number-sections", action="store_true", help="섹션 번호 자동 추가")
    parser.add_argument("--keep-temp", action="store_true", help="변환 후 임시 파일 유지")
    args = parser.parse_args()

    script_file = Path(__file__).resolve()
    css_default, mermaid_script, template_default = _default_paths(script_file)

    css_path = args.css or css_default
    template_path = args.template or (template_default if template_default.exists() else None)
    work_dir = (args.work_dir or (args.output.parent / ".md_pdf_tmp")).resolve()

    try:
        # UTF-8 직독 (플랫폼 ANSI 코드페이지 금지 — CJK mojibake 방지).
        md_text = args.input.read_text(encoding="utf-8")

        # Mermaid 사전 렌더링 (best-effort: mmdc/스크립트 부재·실패 시 WARN 후 코드블록 유지).
        try:
            md_text = _render_mermaids(md_text, work_dir, mermaid_script)
        except Exception as mermaid_exc:  # noqa: BLE001 — degrade, do not abort
            print(f"WARN: mermaid 렌더링 건너뜀 ({mermaid_exc}); 코드블록 유지", file=sys.stderr)

        # A2: 선행 frontmatter 분리 → metadata-file, 본문만 렌더.
        metadata_yaml, body = _split_frontmatter(md_text)

        work_dir.mkdir(parents=True, exist_ok=True)
        tmp_md = work_dir / "processed.md"
        tmp_md.write_text(body, encoding="utf-8")

        metadata_file: Path | None = None
        if metadata_yaml is not None:
            metadata_file = work_dir / "frontmatter.yaml"
            metadata_file.write_text(metadata_yaml, encoding="utf-8")

        args.output.parent.mkdir(parents=True, exist_ok=True)

        _run_pandoc(
            input_md=tmp_md,
            output_pdf=args.output,
            css_path=css_path,
            template_path=template_path,
            metadata_file=metadata_file,
            toc=args.toc,
            number_sections=args.number_sections,
            toc_depth=args.toc_depth,
        )

        # A3: 비-PDF 출하 거부.
        _verify_pdf(args.output)

        if not args.keep_temp and work_dir.exists():
            for p in work_dir.glob("*"):
                p.unlink(missing_ok=True)
            work_dir.rmdir()

        print(str(args.output))
        return 0

    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
