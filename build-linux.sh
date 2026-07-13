#!/usr/bin/env bash
#
# Blockfall — 리눅스 독립 실행판 빌드 스크립트.
#
# 결과물 (dist/ 에 생성):
#   dist/Blockfall-linux-x86_64.tar.gz   폴더형 배포판 (압축)
#   dist/Blockfall.run                   파일 하나로 복사·실행하는 자기추출본
#
# 사용법:
#   ./build-linux.sh              빌드 + 패키징
#   ./build-linux.sh --run        빌드 후 (디스플레이 있으면) 바로 실행
#
# Godot 실행 파일 탐색: 환경변수 GODOT → PATH의 godot/godot4 → ~/.local/godot/*mono*
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

GAME_DIR="game"
PRESET="Linux Desktop"
PKG="Blockfall-linux-x86_64"
DIST="$ROOT/dist"
STAGE="$ROOT/$GAME_DIR/build/stage"
OUT_DIR="$ROOT/$GAME_DIR/build/linux-desktop"
DATA_DIR_NAME="data_Blockfall_linuxbsd_x86_64"

# Godot 4.3 .NET 내보내기 템플릿
GODOT_VER="4.3.stable.mono"
TPL_URL="https://github.com/godotengine/godot/releases/download/4.3-stable/Godot_v4.3-stable_mono_export_templates.tpz"
TPL_DIR="$HOME/.local/share/godot/export_templates/$GODOT_VER"

c_i(){  printf '\033[1;36m[i]\033[0m %s\n' "$*"; }
c_ok(){ printf '\033[1;32m[✓]\033[0m %s\n' "$*"; }
c_err(){ printf '\033[1;31m[✗]\033[0m %s\n' "$*" >&2; }
die(){ c_err "$*"; exit 1; }

# ── .NET (홈 디렉터리 설치본 우선) ───────────────────────────
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
fi
command -v dotnet >/dev/null 2>&1 || die ".NET SDK가 없습니다. https://dot.net/v1/dotnet-install.sh 로 .NET 8 설치."
c_i ".NET SDK: $(dotnet --version)"

# ── Godot 탐색 ───────────────────────────────────────────────
find_godot(){
  local g="" cand
  if [[ -n "${GODOT:-}" ]]; then g="$GODOT"
  elif command -v godot  >/dev/null 2>&1; then g="$(command -v godot)"
  elif command -v godot4 >/dev/null 2>&1; then g="$(command -v godot4)"
  else
    for cand in "$HOME/.local/godot"/*mono*/Godot*mono*.x86_64 \
                "$HOME/.local/godot"/Godot*mono*.x86_64; do
      [[ -x "$cand" ]] && { g="$cand"; break; }
    done
  fi
  [[ -n "$g" && -x "$g" ]] || die "Godot 4.3 .NET/Mono 실행 파일을 찾지 못했습니다. GODOT=... 로 지정하세요."
  GODOT_BIN="$g"
  c_i "Godot: $GODOT_BIN ($("$g" --version 2>/dev/null | head -n1))"
}
find_godot

# ── 내보내기 템플릿 (없으면 자동 설치) ──────────────────────
if [[ ! -f "$TPL_DIR/linux_release.x86_64" ]]; then
  c_i "내보내기 템플릿이 없습니다. 내려받는 중… (약 1GB, 최초 1회)"
  command -v curl >/dev/null 2>&1 || die "curl 이 필요합니다 (또는 템플릿을 Godot 에디터에서 수동 설치)."
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  curl -fL --retry 3 -o "$tmp/tpl.tpz" "$TPL_URL"
  unzip -q "$tmp/tpl.tpz" -d "$tmp"
  mkdir -p "$TPL_DIR"; mv "$tmp"/templates/* "$TPL_DIR"/
  [[ -f "$TPL_DIR/linux_release.x86_64" ]] || die "템플릿 설치 실패."
  c_ok "템플릿 설치 완료: $TPL_DIR"
fi

# ── Godot .NET 내보내기는 project.godot 옆 솔루션을 요구함 ────
if [[ ! -f "$GAME_DIR/Blockfall.sln" ]]; then
  c_i "game/Blockfall.sln 생성 중 (Godot 내보내기용)…"
  ( cd "$GAME_DIR" && dotnet new sln -n Blockfall >/dev/null && dotnet sln Blockfall.sln add Blockfall.csproj >/dev/null )
fi

# ── 프리셋 확인 ──────────────────────────────────────────────
grep -q "name=\"$PRESET\"" "$GAME_DIR/export_presets.cfg" 2>/dev/null \
  || die "export_presets.cfg 에 '$PRESET' 프리셋이 없습니다."

# ── 내보내기 ─────────────────────────────────────────────────
c_i "내보내기: '$PRESET' → $OUT_DIR/"
rm -rf "$OUT_DIR"; mkdir -p "$OUT_DIR"
"$GODOT_BIN" --headless --path "$GAME_DIR" --export-release "$PRESET" "$OUT_DIR/Blockfall.x86_64" \
  2>&1 | grep -viE '^\s*savepack: step' || true
[[ -f "$OUT_DIR/Blockfall.x86_64" && -d "$OUT_DIR/$DATA_DIR_NAME" ]] \
  || die "내보내기 결과가 불완전합니다 (실행파일 또는 어셈블리 폴더 누락)."
c_ok "내보내기 완료."

# ── 스테이징 (packaging/ 의 런처·설명 포함) ─────────────────
c_i "패키지 구성 중…"
rm -rf "$STAGE"; mkdir -p "$STAGE/$PKG"
cp -a "$OUT_DIR/Blockfall.x86_64"     "$STAGE/$PKG"/
cp -a "$OUT_DIR/$DATA_DIR_NAME"       "$STAGE/$PKG"/
cp -a "$ROOT/packaging/Blockfall.sh"  "$STAGE/$PKG"/
cp -a "$ROOT/packaging/읽어주세요.txt" "$STAGE/$PKG"/
chmod +x "$STAGE/$PKG/Blockfall.sh" "$STAGE/$PKG/Blockfall.x86_64"

# ── tar.gz ───────────────────────────────────────────────────
mkdir -p "$DIST"
tar -C "$STAGE" -czf "$DIST/$PKG.tar.gz" "$PKG"
c_ok "생성: dist/$PKG.tar.gz"

# ── 자기추출 .run (헤더 + tar.gz) ────────────────────────────
BUILD_ID="$(sha256sum "$DIST/$PKG.tar.gz" | cut -c1-16)"
RUN="$DIST/Blockfall.run"
sed "s/__BUILD_ID__/$BUILD_ID/" "$ROOT/packaging/selfextract-header.sh" > "$RUN"
cat "$DIST/$PKG.tar.gz" >> "$RUN"
chmod +x "$RUN"
c_ok "생성: dist/Blockfall.run"

echo
c_ok "완료!"
echo "  • dist/$PKG.tar.gz   (폴더형: 풀고 ./Blockfall.sh)"
echo "  • dist/Blockfall.run    (파일 하나: 복사 후 ./Blockfall.run)"

# ── 선택: 바로 실행 ──────────────────────────────────────────
if [[ "${1:-}" == "--run" ]]; then
  if [[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]]; then
    c_i "실행합니다…"; exec "$STAGE/$PKG/Blockfall.sh"
  else
    c_i "디스플레이가 없어 헤드리스 부팅 검증만 수행합니다…"
    exec "$STAGE/$PKG/Blockfall.sh" --headless-check
  fi
fi
