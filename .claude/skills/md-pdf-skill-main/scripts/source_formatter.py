#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from pathlib import Path


CATEGORY_MAP = {
    "official": "🔴 핵심 1차 소스",
    "corporate": "🔴 핵심 1차 소스",
    "analysis": "🟠 핵심 분석 기사",
    "news": "🟡 뉴스/미디어",
    "community": "🟢 커뮤니티/SNS",
    "data_platform": "🔵 데이터 플랫폼",
    "data": "🔵 데이터 플랫폼",
    "other": "🟣 기타",
}


@dataclass
class Source:
    title: str
    url: str
    date: str = ""
    type: str = "other"



def load_sources(path: Path) -> list[Source]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, list):
        raise ValueError("Input JSON must be an array")
    out: list[Source] = []
    for row in payload:
        if not isinstance(row, dict):
            continue
        out.append(
            Source(
                title=str(row.get("title", "(untitled)")),
                url=str(row.get("url", "")),
                date=str(row.get("date", "")),
                type=str(row.get("type", "other")).lower(),
            )
        )
    return out


def to_markdown(sources: list[Source]) -> str:
    grouped: dict[str, list[Source]] = {}
    for s in sources:
        cat = CATEGORY_MAP.get(s.type, "🟣 기타")
        grouped.setdefault(cat, []).append(s)

    order = [
        "🔴 핵심 1차 소스",
        "🟠 핵심 분석 기사",
        "🟡 뉴스/미디어",
        "🟢 커뮤니티/SNS",
        "🔵 데이터 플랫폼",
        "🟣 기타",
    ]

    lines = ["## 전체 출처 목록", ""]
    for cat in order:
        rows = grouped.get(cat, [])
        if not rows:
            continue
        lines.append(f"### {cat}")
        for s in sorted(rows, key=lambda x: (x.date, x.title), reverse=True):
            date_text = f" ({s.date})" if s.date else ""
            lines.append(f"- [{s.title}]({s.url}){date_text}")
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def load_sources_from_stdin() -> list[Source]:
    payload = json.loads(sys.stdin.read())
    if not isinstance(payload, list):
        raise ValueError("Input JSON must be an array")
    out: list[Source] = []
    for row in payload:
        if not isinstance(row, dict):
            continue
        out.append(
            Source(
                title=str(row.get("title", "(untitled)")),
                url=str(row.get("url", "")),
                date=str(row.get("date", "")),
                type=str(row.get("type", "other")).lower(),
            )
        )
    return out


def main() -> int:
    parser = argparse.ArgumentParser(description="Format source list into categorized markdown")
    parser.add_argument("--input", type=str, default=None,
                        help="JSON 파일 경로. '-' 또는 생략 시 stdin에서 읽음")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    if args.input is None or args.input == "-":
        sources = load_sources_from_stdin()
    else:
        sources = load_sources(Path(args.input))

    md = to_markdown(sources)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(md, encoding="utf-8")
    print(str(args.output))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
