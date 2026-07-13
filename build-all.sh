#!/usr/bin/env bash
#
# Blockfall — 전 플랫폼 빌드 스크립트 (하나로).
#
#   중요: "모든 OS에서 도는 실행 파일 하나"는 존재할 수 없습니다. OS마다 실행
#   파일 포맷/CPU가 다릅니다. 이 스크립트 하나가 각 OS에 맞는 실행 파일을 각각
#   만들어 dist/ 에 넣어줍니다. 각 파일은 자기 OS 안에서 환경에 맞게 동작합니다.
#
# 사용법:
#   ./build-all.sh                  이 PC에서 가능한 모든 타깃을 빌드
#   ./build-all.sh linux windows    지정한 타깃만 빌드
#   타깃: linux windows macos android ios
#
# 이 리눅스 PC에서 만들 수 있는 것:
#   linux   ✓        windows ✓        macos ✓(미서명)
#   android ⚠ Android SDK/JDK/키스토어 필요
#   ios     ✗ macOS + Xcode 필수 — Mac에서 ./build-ios.sh 실행 (docs/IOS_RELEASE.md)
#
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; cd "$ROOT"
GAME_DIR="game"
DIST="$ROOT/dist"
BUILD="$ROOT/$GAME_DIR/build"
GODOT_VER="4.3.stable.mono"
TPL_URL="https://github.com/godotengine/godot/releases/download/4.3-stable/Godot_v4.3-stable_mono_export_templates.tpz"
TPL_DIR="$HOME/.local/share/godot/export_templates/$GODOT_VER"

c_i(){  printf '\033[1;36m[i]\033[0m %s\n' "$*"; }
c_ok(){ printf '\033[1;32m[✓]\033[0m %s\n' "$*"; }
c_warn(){ printf '\033[1;33m[!]\033[0m %s\n' "$*"; }
c_err(){ printf '\033[1;31m[✗]\033[0m %s\n' "$*" >&2; }
die(){ c_err "$*"; exit 1; }

SUMMARY=()
note(){ SUMMARY+=("$1"); }

# ── 타깃 선택 ────────────────────────────────────────────────
ALL=(linux windows macos android ios)
if [[ $# -gt 0 ]]; then TARGETS=("$@"); else TARGETS=("${ALL[@]}"); fi
want(){ local t; for t in "${TARGETS[@]}"; do [[ "$t" == "$1" ]] && return 0; done; return 1; }

# ── .NET ─────────────────────────────────────────────────────
if [[ -x "$HOME/.dotnet/dotnet" ]]; then export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"; fi
command -v dotnet >/dev/null 2>&1 || die ".NET SDK 없음 (curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0)"
c_i ".NET SDK: $(dotnet --version)"

# ── Godot 탐색 ───────────────────────────────────────────────
GODOT_BIN=""
if [[ -n "${GODOT:-}" ]]; then GODOT_BIN="$GODOT"
elif command -v godot  >/dev/null 2>&1; then GODOT_BIN="$(command -v godot)"
elif command -v godot4 >/dev/null 2>&1; then GODOT_BIN="$(command -v godot4)"
else for c in "$HOME/.local/godot"/*mono*/Godot*mono*.x86_64 "$HOME/.local/godot"/Godot*mono*.x86_64; do
       [[ -x "$c" ]] && { GODOT_BIN="$c"; break; }; done
fi
[[ -n "$GODOT_BIN" && -x "$GODOT_BIN" ]] || die "Godot 4.3 .NET/Mono 실행 파일을 못 찾음. GODOT=... 로 지정."
c_i "Godot: $GODOT_BIN ($("$GODOT_BIN" --version 2>/dev/null | head -n1))"

# ── 내보내기 템플릿 (없으면 자동 설치) ──────────────────────
if [[ ! -f "$TPL_DIR/linux_release.x86_64" ]]; then
  c_i "내보내기 템플릿 내려받는 중… (약 1GB, 최초 1회)"
  command -v curl >/dev/null 2>&1 || die "curl 필요."
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  curl -fL --retry 3 -o "$tmp/t.tpz" "$TPL_URL"; unzip -q "$tmp/t.tpz" -d "$tmp"
  mkdir -p "$TPL_DIR"; mv "$tmp"/templates/* "$TPL_DIR"/
  [[ -f "$TPL_DIR/linux_release.x86_64" ]] || die "템플릿 설치 실패."
fi

# ── Godot .NET 내보내기는 project.godot 옆 솔루션을 요구함 ────
if [[ ! -f "$GAME_DIR/Blockfall.sln" ]]; then
  ( cd "$GAME_DIR" && dotnet new sln -n Blockfall >/dev/null && dotnet sln Blockfall.sln add Blockfall.csproj >/dev/null )
fi

# ── 리소스 임포트(1회) ───────────────────────────────────────
c_i "리소스 임포트 중…"
"$GODOT_BIN" --headless --path "$GAME_DIR" --import >/dev/null 2>&1 || true

mkdir -p "$DIST"

# ── Android 툴체인 자동 탐지 (있으면 PATH에 올림) ─────────────
#    설치 위치는 사용자 홈 기준: JDK ~/.local/jdk-17, SDK ~/Android/Sdk.
#    Godot 에디터 설정(editor_settings-4.3.tres)에도 같은 경로가 박혀 있음.
if [[ -z "${JAVA_HOME:-}" && -x "$HOME/.local/jdk-17/bin/java" ]]; then
  export JAVA_HOME="$HOME/.local/jdk-17"; export PATH="$JAVA_HOME/bin:$PATH"
fi
if [[ -z "${ANDROID_SDK_ROOT:-}" && -d "$HOME/Android/Sdk/platform-tools" ]]; then
  export ANDROID_SDK_ROOT="$HOME/Android/Sdk"; export ANDROID_HOME="$ANDROID_SDK_ROOT"
fi

# ── 공통: 내보내기 실행 ──────────────────────────────────────
#   NOISE 필터: 헤드리스 export 중 Godot이 뿜는 무해한 로그를 걸러낸다.
#   - "savepack: step"           패킹 진행 스텝(수백 줄)
#   - "_EDITOR_GET"/"editor_settings"/"EditorSettings::get_singleton"
#     헤드리스엔 에디터 설정이 없어 옵션 조회가 빈 값을 반환할 때 나오는 줄.
#     빌드 결과에는 영향 없음(모든 산출물 정상 생성). 실제 export 실패는 다른
#     메시지로 찍히고, 각 build_* 함수가 산출물 존재를 따로 검증한다.
NOISE='^\s*savepack: step|_EDITOR_GET|editor_settings|EditorSettings::get_singleton'
export_preset(){ # $1=preset  $2=out-file
  "$GODOT_BIN" --headless --path "$GAME_DIR" --export-release "$1" "$2" \
    2>&1 | grep -viE "$NOISE" || true
}
export_preset_debug(){ # $1=preset  $2=out-file  (디버그 키스토어로 서명)
  "$GODOT_BIN" --headless --path "$GAME_DIR" --export-debug "$1" "$2" \
    2>&1 | grep -viE "$NOISE" || true
}

# ── LINUX ────────────────────────────────────────────────────
build_linux(){
  c_i "── Linux 빌드 ──"
  local out="$BUILD/linux-desktop" pkg="Blockfall-linux-x86_64" stage
  rm -rf "$out"; mkdir -p "$out"
  export_preset "Linux Desktop" "$out/Blockfall.x86_64"
  if [[ ! -f "$out/Blockfall.x86_64" || ! -d "$out/data_Blockfall_linuxbsd_x86_64" ]]; then
    c_err "Linux 내보내기 실패"; note "linux   ✗ 내보내기 실패"; return 1; fi
  stage="$BUILD/stage/$pkg"; rm -rf "$stage"; mkdir -p "$stage"
  cp -a "$out/Blockfall.x86_64" "$out/data_Blockfall_linuxbsd_x86_64" "$stage"/
  cp -a "$ROOT/packaging/Blockfall.sh" "$stage/Blockfall.sh"
  cp -a "$ROOT/packaging/읽어주세요.txt" "$stage/읽어주세요.txt"
  chmod +x "$stage/Blockfall.sh" "$stage/Blockfall.x86_64"
  tar -C "$BUILD/stage" -czf "$DIST/$pkg.tar.gz" "$pkg"
  # 자기추출 .run (파일 하나로 복사·실행)
  local bid; bid="$(sha256sum "$DIST/$pkg.tar.gz" | cut -c1-16)"
  sed "s/__BUILD_ID__/$bid/" "$ROOT/packaging/selfextract-header.sh" > "$DIST/Blockfall.run"
  cat "$DIST/$pkg.tar.gz" >> "$DIST/Blockfall.run"; chmod +x "$DIST/Blockfall.run"
  c_ok "Linux: dist/$pkg.tar.gz , dist/Blockfall.run"
  note "linux   ✓ dist/$pkg.tar.gz  +  dist/Blockfall.run"
}

# ── WINDOWS ──────────────────────────────────────────────────
build_windows(){
  c_i "── Windows 빌드 ──"
  local out="$BUILD/windows-desktop" pkg="Blockfall-windows-x86_64" stage
  rm -rf "$out"; mkdir -p "$out"
  export_preset "Windows Desktop" "$out/Blockfall.exe"
  if [[ ! -f "$out/Blockfall.exe" || ! -d "$out/data_Blockfall_windows_x86_64" ]]; then
    c_err "Windows 내보내기 실패"; note "windows ✗ 내보내기 실패"; return 1; fi
  stage="$BUILD/stage/$pkg"; rm -rf "$stage"; mkdir -p "$stage"
  cp -a "$out/Blockfall.exe" "$out/data_Blockfall_windows_x86_64" "$stage"/
  cp -a "$ROOT/packaging/README-windows.txt" "$stage/읽어주세요.txt"
  ( cd "$BUILD/stage" && rm -f "$DIST/$pkg.zip" && zip -qr "$DIST/$pkg.zip" "$pkg" )
  c_ok "Windows: dist/$pkg.zip"
  note "windows ✓ dist/$pkg.zip  (Blockfall.exe 더블클릭)"
}

# ── macOS ────────────────────────────────────────────────────
build_macos(){
  c_i "── macOS 빌드 ──"
  local out="$BUILD/macos-desktop" zipout="$DIST/Blockfall-macos-universal.zip"
  rm -rf "$out"; mkdir -p "$out"
  export_preset "macOS Desktop" "$out/Blockfall.zip"
  if [[ ! -f "$out/Blockfall.zip" ]]; then
    c_err "macOS 내보내기 실패"; note "macos   ✗ 내보내기 실패"; return 1; fi
  cp -f "$out/Blockfall.zip" "$zipout"
  ( cd "$ROOT/packaging" && zip -qj "$zipout" "README-macos.txt" )
  c_ok "macOS: dist/$(basename "$zipout")  (미서명 — 우클릭>열기)"
  note "macos   ✓ dist/$(basename "$zipout")  (미서명: 우클릭>열기)"
}

# ── ANDROID ──────────────────────────────────────────────────
build_android(){
  c_i "── Android 빌드 ──"
  local sdk="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
  local miss=()
  [[ -n "$sdk" && -d "$sdk" ]] || miss+=("Android SDK (ANDROID_SDK_ROOT 미설정)")
  command -v java >/dev/null 2>&1 || miss+=("JDK 17 (java 없음)")
  [[ -f "$HOME/.android/debug.keystore" ]] || miss+=("디버그 키스토어(~/.android/debug.keystore)")
  [[ -d "$GAME_DIR/android/build" ]] || miss+=("Godot 안드로이드 빌드 템플릿(에디터에서 설치)")
  if (( ${#miss[@]} )); then
    c_warn "Android 빌드에 필요한 요소가 없어 건너뜁니다:"
    printf '        - %s\n' "${miss[@]}"
    note "android ⏭ 건너뜀 — 아래 안내 참고 (SDK/JDK/키스토어/빌드템플릿 필요)"
    return 0
  fi
  # 디버그 서명 APK: 폰에 바로 설치해 테스트하는 용도.
  # Play 스토어 제출용은 릴리스 키스토어를 만들고 프리셋에 keystore/keystore_user/
  # keystore_pass 를 채운 뒤 릴리스(.aab, export_format=1) 내보내기로 전환할 것.
  local out="$BUILD/android"; rm -rf "$out"; mkdir -p "$out"
  export_preset_debug "Android" "$out/Blockfall.apk"
  if [[ -f "$out/Blockfall.apk" ]]; then
    cp -f "$out/Blockfall.apk" "$DIST/Blockfall-android-debug.apk"
    c_ok "Android: dist/Blockfall-android-debug.apk (디버그 서명 — 사이드로드/테스트용)"
    note "android ✓ dist/Blockfall-android-debug.apk  (adb install 또는 폰에 복사)"
  else
    c_err "Android 내보내기 실패 — 위 로그를 확인하세요."
    note "android ✗ 내보내기 실패"
  fi
}

# ── iOS ──────────────────────────────────────────────────────
build_ios(){
  c_i "── iOS 빌드 ──"
  if [[ "$(uname -s)" != "Darwin" ]]; then
    c_warn "iOS는 macOS + Xcode에서만 빌드할 수 있습니다 (현재 $(uname -s))."
    c_warn "→ Mac 없이: Codemagic 클라우드 빌드 (codemagic.yaml, docs/IOS_RELEASE.md 방법 A)"
    c_warn "→ Mac 있음: 저장소를 옮겨 ./build-ios.sh 실행 (docs/IOS_RELEASE.md 방법 B)"
    note "ios     ⏭ Codemagic 클라우드 빌드 또는 Mac에서 ./build-ios.sh (docs/IOS_RELEASE.md)"
    return 0
  fi
  # macOS에서는 전용 스크립트로 위임 — Xcode 프로젝트 생성 + 다음 단계 안내까지.
  if "$ROOT/build-ios.sh"; then
    note "ios     ✓ Xcode 프로젝트 생성 — Xcode에서 Archive→Upload (docs/IOS_RELEASE.md)"
  else
    note "ios     ✗ 실패 — ./build-ios.sh 로그 확인"
  fi
}

# ── 실행 ─────────────────────────────────────────────────────
want linux   && build_linux   || true
want windows && build_windows || true
want macos   && build_macos   || true
want android && build_android || true
want ios     && build_ios     || true

echo
c_ok "빌드 요약 ─────────────────────────────"
for line in "${SUMMARY[@]}"; do echo "  $line"; done
echo
c_i "결과물은 dist/ 에 있습니다. 각 OS의 사용자는 자기 OS용 파일만 받으면 됩니다."
