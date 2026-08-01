#!/usr/bin/env python3
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


def _find_mmdc() -> str:
    mmdc = shutil.which("mmdc")
    if not mmdc:
        raise FileNotFoundError("mmdc not found. Install with: npm i -g @mermaid-js/mermaid-cli")
    return mmdc


def render_mermaid(input_path: Path, output_path: Path, width: int = 1600, scale: float = 2.0, background: str = "white") -> None:
    mmdc = _find_mmdc()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    cmd = [
        mmdc,
        "-i",
        str(input_path),
        "-o",
        str(output_path),
        "-w",
        str(width),
        "-s",
        str(scale),
        "-b",
        background,
        "-t",
        "default",
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"mmdc failed: {proc.stderr.strip() or proc.stdout.strip()}")



def main() -> int:
    parser = argparse.ArgumentParser(description="Render Mermaid text/file to image")
    parser.add_argument("--input", type=Path, help="Path to .mmd file")
    parser.add_argument("--code", type=str, help="Inline mermaid code")
    parser.add_argument("--output", type=Path, required=True, help="Output image path (.png/.svg)")
    parser.add_argument("--width", type=int, default=1600)
    parser.add_argument("--scale", type=float, default=2.0)
    parser.add_argument("--background", type=str, default="white")
    args = parser.parse_args()

    if bool(args.input) == bool(args.code):
        print("Provide exactly one of --input or --code", file=sys.stderr)
        return 2

    try:
        if args.input:
            render_mermaid(args.input, args.output, args.width, args.scale, args.background)
        else:
            with tempfile.TemporaryDirectory(prefix="mermaid-") as td:
                src = Path(td) / "inline.mmd"
                src.write_text(args.code, encoding="utf-8")
                render_mermaid(src, args.output, args.width, args.scale, args.background)
        print(str(args.output))
        return 0
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
