#!/usr/bin/env bash
#
# Blockfall — iOS 빌드 스크립트 (반드시 macOS에서 실행).
#
#   이 스크립트는 Godot로 Xcode 프로젝트를 생성하는 데까지 자동화합니다.
#   서명·아카이브·App Store 업로드는 Xcode(자동 서명)에서 마무리합니다.
#   전체 출시 절차: docs/IOS_RELEASE.md
#
# 사용법 (Mac 터미널에서):
#   ./build-ios.sh --team ABCDE12345          # Team ID 주입 + Xcode 프로젝트 생성
#   ./build-ios.sh --team ABCDE12345 --open   # 생성 후 Xcode 바로 열기
#   ./build-ios.sh --bundle com.myname.blockfall --team ABCDE12345
#   ./build-ios.sh                            # 프리셋에 Team ID가 이미 있으면 그대로 진행
#
# 준비물 (docs/IOS_RELEASE.md 1장 참고):
#   - Xcode (App Store에서 설치, 최초 1회 실행해 라이선스 동의)
#   - .NET 8 SDK        https://dotnet.microsoft.com/download/dotnet/8.0
#   - Godot 4.3 (.NET)  https://godotengine.org/download/archive/4.3-stable/
#     → Godot_mono.app 을 /Applications 에 설치
#
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; cd "$ROOT"
GAME_DIR="game"
OUT="$ROOT/$GAME_DIR/build/ios"
PRESETS="$GAME_DIR/export_presets.cfg"
GODOT_VER="4.3.stable.mono"
TPL_DIR="$HOME/Library/Application Support/Godot/export_templates/$GODOT_VER"
TPL_URL="https://github.com/godotengine/godot/releases/download/4.3-stable/Godot_v4.3-stable_mono_export_templates.tpz"

c_i(){  printf '\033[1;36m[i]\033[0m %s\n' "$*"; }
c_ok(){ printf '\033[1;32m[✓]\033[0m %s\n' "$*"; }
c_warn(){ printf '\033[1;33m[!]\033[0m %s\n' "$*"; }
c_err(){ printf '\033[1;31m[✗]\033[0m %s\n' "$*" >&2; }
die(){ c_err "$*"; exit 1; }

# ── 인자 ─────────────────────────────────────────────────────
TEAM_ID="${TEAM_ID:-}"; BUNDLE_ID=""; OPEN_XCODE=0
while [[ $# -gt 0 ]]; do case "$1" in
  --team)   TEAM_ID="${2:-}"; shift 2;;
  --bundle) BUNDLE_ID="${2:-}"; shift 2;;
  --open)   OPEN_XCODE=1; shift;;
  -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0;;
  *) die "알 수 없는 옵션: $1 (--help 참고)";;
esac; done

# ── 환경 점검 ────────────────────────────────────────────────
[[ "$(uname -s)" == "Darwin" ]] || die "iOS 빌드는 macOS에서만 가능합니다 (현재: $(uname -s)). 이 저장소를 Mac으로 옮겨 실행하세요."
xcode-select -p >/dev/null 2>&1 || die "Xcode가 없습니다. App Store에서 Xcode 설치 후: xcode-select --install"
command -v xcodebuild >/dev/null 2>&1 || die "xcodebuild 없음 — Xcode를 한 번 실행해 초기 설정을 마치세요."

if ! command -v dotnet >/dev/null 2>&1; then
  for d in "$HOME/.dotnet" "/usr/local/share/dotnet"; do
    [[ -x "$d/dotnet" ]] && { export DOTNET_ROOT="$d"; export PATH="$d:$PATH"; break; }
  done
fi
command -v dotnet >/dev/null 2>&1 || die ".NET 8 SDK 없음 → https://dotnet.microsoft.com/download/dotnet/8.0"
c_i ".NET SDK: $(dotnet --version)"

# Godot 4.3 .NET(mono) 에디터 탐색
GODOT_BIN="${GODOT:-}"
if [[ -z "$GODOT_BIN" ]]; then
  for c in "/Applications/Godot_mono.app/Contents/MacOS/Godot" \
           "$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot" \
           "/Applications/Godot.app/Contents/MacOS/Godot"; do
    [[ -x "$c" ]] && { GODOT_BIN="$c"; break; }
  done
fi
[[ -n "$GODOT_BIN" && -x "$GODOT_BIN" ]] || die "Godot 4.3 (.NET) 에디터를 못 찾음. Godot_mono.app 을 /Applications 에 설치하거나 GODOT=/path/to/Godot 로 지정."
"$GODOT_BIN" --version 2>/dev/null | head -n1 | grep -q "mono" || die "찾은 Godot이 .NET(mono) 빌드가 아닙니다: $GODOT_BIN — 반드시 '.NET' 버전을 설치하세요."
c_i "Godot: $GODOT_BIN ($("$GODOT_BIN" --version 2>/dev/null | head -n1))"

# 내보내기 템플릿 (없으면 자동 설치, 약 1GB 최초 1회)
if [[ ! -f "$TPL_DIR/ios.zip" ]]; then
  c_i "내보내기 템플릿 내려받는 중… (약 1GB, 최초 1회)"
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  curl -fL --retry 3 -o "$tmp/t.tpz" "$TPL_URL" || die "템플릿 다운로드 실패"
  unzip -q "$tmp/t.tpz" -d "$tmp"
  mkdir -p "$TPL_DIR"; mv "$tmp"/templates/* "$TPL_DIR"/
  [[ -f "$TPL_DIR/ios.zip" ]] || die "템플릿 설치 실패."
fi

# ── Team ID / Bundle ID를 프리셋에 주입 ──────────────────────
bsd_sed(){ sed -i '' "$@"; } # macOS(BSD) sed
if [[ -n "$TEAM_ID" ]]; then
  [[ "$TEAM_ID" =~ ^[A-Z0-9]{10}$ ]] || c_warn "Team ID '$TEAM_ID' 형식이 특이합니다 (보통 대문자/숫자 10자리 — developer.apple.com > Membership 확인)."
  bsd_sed "s|^application/app_store_team_id=.*|application/app_store_team_id=\"$TEAM_ID\"|" "$PRESETS"
  c_ok "Team ID 설정: $TEAM_ID"
fi
if [[ -n "$BUNDLE_ID" ]]; then
  # iOS 프리셋([preset.4.options]) 범위의 bundle_identifier만 교체
  awk -v b="$BUNDLE_ID" '
    /^\[preset\.4\.options\]/{ios=1} /^\[preset\.[0-9]+\]/{if($0!~/preset\.4\]/)ios=0}
    ios && /^application\/bundle_identifier=/{print "application/bundle_identifier=\"" b "\""; next}
    {print}' "$PRESETS" > "$PRESETS.tmp" && mv "$PRESETS.tmp" "$PRESETS"
  c_ok "Bundle ID 설정: $BUNDLE_ID"
fi
grep -A30 '^\[preset\.4\.options\]' "$PRESETS" | grep -q 'app_store_team_id=""' && \
  c_warn "Team ID가 비어 있습니다 — Xcode에서 팀을 직접 선택해야 합니다 (--team XXXXXXXXXX 권장)."

# ── 내보내기 ─────────────────────────────────────────────────
if [[ ! -f "$GAME_DIR/Blockfall.sln" ]]; then
  ( cd "$GAME_DIR" && dotnet new sln -n Blockfall >/dev/null && dotnet sln Blockfall.sln add Blockfall.csproj >/dev/null )
fi
c_i "리소스 임포트 중…"
"$GODOT_BIN" --headless --path "$GAME_DIR" --import >/dev/null 2>&1 || true

c_i "iOS Xcode 프로젝트 생성 중… (C# AOT 컴파일 포함, 몇 분 걸릴 수 있음)"
rm -rf "$OUT"; mkdir -p "$OUT"
NOISE='^\s*savepack: step|_EDITOR_GET|editor_settings|EditorSettings::get_singleton'
"$GODOT_BIN" --headless --path "$GAME_DIR" --export-release "iOS" "$OUT/Blockfall.ipa" \
  2>&1 | grep -viE "$NOISE" || true

XCPROJ="$(find "$OUT" -maxdepth 2 -name '*.xcodeproj' | head -n1)"
[[ -n "$XCPROJ" ]] || die "Xcode 프로젝트가 생성되지 않았습니다 — 위 로그를 확인하세요. (프리셋 export_project_only=true 기준)"

c_ok "Xcode 프로젝트 생성 완료: $XCPROJ"
echo
c_i "다음 단계 (docs/IOS_RELEASE.md 3~5장):"
echo "    1) open \"$XCPROJ\"   ← Xcode로 열기"
echo "    2) 좌측 프로젝트 → Signing & Capabilities → Team 선택 (자동 서명 ON)"
echo "    3) 상단 기기 선택: Any iOS Device (arm64)"
echo "    4) Product → Archive → Distribute App → App Store Connect → Upload"
echo "    5) App Store Connect(TestFlight)에서 빌드 확인 → 심사 제출"
echo
[[ $OPEN_XCODE -eq 1 ]] && open "$XCPROJ"
exit 0
