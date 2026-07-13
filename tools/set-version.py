#!/usr/bin/env python3
"""Blockfall 버전 단일 소스 도구.

export_presets.cfg 안 각 플랫폼 프리셋의 버전 필드를 한 번에 맞춰준다. CD에서는
git 태그(vX.Y.Z)를 마케팅 버전으로, CI 빌드 번호를 빌드 번호로 주입한다 —
버전을 손으로 여러 군데 고치다 어긋나는 사고를 없애는 것이 목적.

  마케팅 버전(--version X.Y.Z) → 사용자에게 보이는 버전:
      iOS   (preset.4)  application/short_version
      Android(preset.3) version/name
      macOS (preset.1,7) application/short_version, application/version
      Win   (preset.0,6) application/file_version, application/product_version  ("X.Y.Z.0")
  빌드 번호(--build N) → 스토어 업로드마다 증가해야 하는 정수:
      iOS   (preset.4)  application/version      (CFBundleVersion)
      Android(preset.3) version/code

사용:
  python3 tools/set-version.py --version 1.0.1 --build 12
  python3 tools/set-version.py --version 1.0.1          # 마케팅 버전만
  python3 tools/set-version.py --build 12               # 빌드 번호만
  python3 tools/set-version.py --version 1.0.1 --print  # 실제 쓰기 없이 미리보기
"""
import argparse
import re
import sys
from pathlib import Path

# 프리셋 인덱스 → 플랫폼 (export_presets.cfg 기준)
IOS, ANDROID = "preset.4.options", "preset.3.options"
MACOS = ("preset.1.options", "preset.7.options")
WINDOWS = ("preset.0.options", "preset.6.options")


def build_edits(marketing: str | None, build: str | None) -> dict[str, dict[str, str]]:
    """(섹션 → {키: 새 줄}) 매핑. 값은 완성된 한 줄(따옴표 포함)."""
    edits: dict[str, dict[str, str]] = {}

    def q(section: str, key: str, line: str):
        edits.setdefault(section, {})[key] = line

    if marketing is not None:
        winver = f"{marketing}.0"  # 윈도우는 4자리 (X.Y.Z.0)
        q(IOS, "application/short_version", f'application/short_version="{marketing}"')
        q(ANDROID, "version/name", f'version/name="{marketing}"')
        for s in MACOS:
            q(s, "application/short_version", f'application/short_version="{marketing}"')
            q(s, "application/version", f'application/version="{marketing}"')
        for s in WINDOWS:
            q(s, "application/file_version", f'application/file_version="{winver}"')
            q(s, "application/product_version", f'application/product_version="{winver}"')

    if build is not None:
        q(IOS, "application/version", f'application/version="{build}"')  # CFBundleVersion
        q(ANDROID, "version/code", f"version/code={build}")  # 정수, 따옴표 없음

    return edits


def apply(cfg: Path, edits: dict[str, dict[str, str]], do_write: bool) -> list[str]:
    """섹션을 추적하며 라인 단위로 교체. 이미 존재하는 키만 바꾼다."""
    lines = cfg.read_text(encoding="utf-8").splitlines(keepends=True)
    header = re.compile(r"^\[(preset\.\d+(?:\.options)?)\]\s*$")
    section = None
    changed: list[str] = []
    seen: dict[str, set[str]] = {s: set() for s in edits}

    for i, line in enumerate(lines):
        m = header.match(line)
        if m:
            section = m.group(1)
            continue
        if section in edits:
            key = line.split("=", 1)[0].strip() if "=" in line else None
            if key in edits[section]:
                new = edits[section][key]
                eol = "\n" if line.endswith("\n") else ""
                if line.rstrip("\n") != new:
                    changed.append(f"[{section}] {new}")
                lines[i] = new + eol
                seen[section].add(key)

    # 기대한 키가 프리셋에 없으면 경고 (프리셋 구조가 바뀐 신호)
    for s, keys in edits.items():
        for k in keys:
            if k not in seen.get(s, set()):
                print(f"경고: [{s}] 에서 '{k}' 키를 찾지 못함 — 건너뜀", file=sys.stderr)

    if do_write:
        cfg.write_text("".join(lines), encoding="utf-8")
    return changed


def main() -> int:
    ap = argparse.ArgumentParser(description="Blockfall export 버전 일괄 설정")
    ap.add_argument("--version", help="마케팅 버전 X.Y.Z (사용자에게 보이는 버전)")
    ap.add_argument("--build", help="빌드 번호(정수) — 스토어 업로드마다 증가")
    ap.add_argument("--file", default="game/export_presets.cfg", help="export_presets.cfg 경로")
    ap.add_argument("--print", dest="preview", action="store_true", help="쓰지 않고 변경 예정만 출력")
    a = ap.parse_args()

    if a.version is None and a.build is None:
        ap.error("--version 또는 --build 중 최소 하나는 필요합니다")
    if a.version is not None and not re.fullmatch(r"\d+\.\d+(\.\d+)?", a.version):
        ap.error(f"버전 형식 오류: '{a.version}' (예: 1.0.1)")
    if a.build is not None and not re.fullmatch(r"\d+", a.build):
        ap.error(f"빌드 번호는 정수여야 합니다: '{a.build}'")

    cfg = Path(a.file)
    if not cfg.is_file():
        print(f"오류: 파일 없음 {cfg} (저장소 루트에서 실행하세요)", file=sys.stderr)
        return 1

    edits = build_edits(a.version, a.build)
    changed = apply(cfg, edits, do_write=not a.preview)
    tag = "미리보기" if a.preview else "적용"
    if changed:
        print(f"[{tag}] 버전 필드 {len(changed)}개:")
        for c in changed:
            print(f"  {c}")
    else:
        print(f"[{tag}] 이미 원하는 값 — 변경 없음")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
