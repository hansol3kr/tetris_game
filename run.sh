#!/usr/bin/env bash
#
# Blockfall — 한 번에 실행 스크립트 (Linux / macOS 공용)
#
#   ./run.sh            코어 빌드 후 게임 실행 (기본)
#   ./run.sh --test     코어 유닛 테스트만 실행 (Godot 불필요)
#   ./run.sh --editor   실행 대신 Godot 에디터로 열기
#   ./run.sh --headless 창 없이 C# 솔루션 빌드만 검증
#   ./run.sh --smoke    창 없이 전 화면·게임플레이 오토플레이 스모크 테스트 (버그/레이아웃 검사)
#
# Godot 실행 파일은 아래 순서로 탐색합니다:
#   1) 환경변수 GODOT
#        Linux : GODOT=/path/to/Godot_v4.3-stable_mono_linux.x86_64 ./run.sh
#        macOS : GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot ./run.sh
#   2) PATH 상의 godot / godot4
#   3) 표준 설치 위치 (Linux: ~/.local/godot,  macOS: /Applications·~/Applications 의 .app)
#
set -euo pipefail

# 저장소 루트 = 이 스크립트가 있는 위치
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

SLN="Blockfall.sln"
GAME_DIR="game"
OS="$(uname -s)"   # Linux | Darwin(macOS)

# ── 색상 로그 ────────────────────────────────────────────────
c_info()  { printf '\033[1;36m[i]\033[0m %s\n' "$*"; }
c_ok()    { printf '\033[1;32m[✓]\033[0m %s\n' "$*"; }
c_err()   { printf '\033[1;31m[✗]\033[0m %s\n' "$*" >&2; }

die() { c_err "$*"; exit 1; }

# GUI 실행 시 Godot에 추가로 넘길 디스플레이 인자 (필요 시 채워짐)
GODOT_DISPLAY_ARGS=()

# ── 그래픽 세션 자동 탐지 ────────────────────────────────────
# SSH/원격 터미널에는 DISPLAY/WAYLAND_DISPLAY가 없다. 하지만 같은 사용자가
# 이 PC 모니터에서 데스크톱에 로그인해 두었다면, 그 그래픽 세션의 환경을
# 찾아와서 게임을 그 모니터에 띄울 수 있다. (소켓은 같은 uid라 접근 가능)
adopt_graphical_session() {
  # 이미 현재 셸에 디스플레이가 있으면 그대로 사용
  [[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]] && return 0

  local uid runtime
  uid="$(id -u)"
  runtime="${XDG_RUNTIME_DIR:-/run/user/$uid}"
  [[ -d "$runtime" ]] || return 1
  export XDG_RUNTIME_DIR="$runtime"

  # 1) systemd 유저 매니저가 보관한 그래픽 환경 변수
  #    (GNOME 등이 로그인 시 DISPLAY/WAYLAND_DISPLAY/XAUTHORITY를 등록)
  local envout d w x
  envout="$(systemctl --user show-environment 2>/dev/null || true)"
  if [[ -n "$envout" ]]; then
    d="$(sed -n 's/^DISPLAY=//p'         <<<"$envout" | head -n1)"
    w="$(sed -n 's/^WAYLAND_DISPLAY=//p' <<<"$envout" | head -n1)"
    x="$(sed -n 's/^XAUTHORITY=//p'      <<<"$envout" | head -n1)"
    [[ -n "$x" ]] && export XAUTHORITY="$x"
    [[ -n "$d" ]] && export DISPLAY="$d"
    [[ -n "$w" ]] && export WAYLAND_DISPLAY="$w"
    [[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]] && return 0
  fi

  # 2) Wayland 소켓 직접 탐색 (systemd env가 비어 있을 때 대비)
  local sock
  for sock in "$runtime"/wayland-[0-9]*; do
    [[ -S "$sock" ]] && { export WAYLAND_DISPLAY="$(basename "$sock")"; return 0; }
  done

  return 1
}

# ── 그래픽 디스플레이 확인 (GUI 실행/에디터 모드) ────────────
# 디스플레이가 없으면 Godot이 디스플레이 서버 생성 실패 후 세그폴트한다.
# 미리 감지해서 세그폴트 대신 명확한 안내를 준다.
require_display() {
  # macOS는 항상 네이티브(Aqua) 디스플레이가 있으므로 검사 불필요
  [[ "$OS" == "Darwin" ]] && return 0

  adopt_graphical_session || true

  if [[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]]; then
    # X11(XWayland 포함)이 있으면 그걸 우선 사용, Wayland만 있으면 wayland 드라이버 지정
    local shown=""
    [[ -n "${WAYLAND_DISPLAY:-}" ]] && shown="Wayland(${WAYLAND_DISPLAY})"
    [[ -n "${DISPLAY:-}" ]] && shown="${shown:+$shown, }X11(${DISPLAY})"
    if [[ -z "${DISPLAY:-}" && -n "${WAYLAND_DISPLAY:-}" ]]; then
      GODOT_DISPLAY_ARGS=(--display-driver wayland)
    fi
    c_info "디스플레이 감지: $shown"
    return 0
  fi

  c_err "그래픽 디스플레이가 없어 게임 창을 띄울 수 없습니다 (DISPLAY / WAYLAND_DISPLAY 미설정)."
  cat >&2 <<'EOF'

  지금은 SSH/원격 터미널 등 화면이 없는 세션이고, 이 PC에 로그인된
  데스크톱 그래픽 세션도 찾지 못했습니다 (모니터에 GDM 로그인 화면만 떠 있는 상태).

  ▶ 해결: 이 PC 모니터 앞에서 데스크톱에 로그인하세요. 그 다음:
      • 데스크톱 안의 터미널에서:  ./run.sh
      • 또는 지금 이 SSH 창에서 그대로:  ./run.sh
        (로그인 후에는 run.sh가 그 그래픽 세션을 자동으로 찾아 모니터에 띄웁니다)

  화면 없이 확인만 하려면:
    ./run.sh --test       # 코어 로직 테스트 (131개)
    ./run.sh --headless   # 창 없이 빌드/부팅 검증
EOF
  exit 1
}

# ── .NET SDK 확인 ────────────────────────────────────────────
# 우분투 26.04는 apt로 .NET 8을 제공하지 않아, 홈 디렉터리(~/.dotnet)에
# 설치된 .NET 8을 우선 사용한다. (없으면 설치 안내)
check_dotnet() {
  # 홈에 .NET 8 SDK가 있으면 PATH 앞에 붙여 시스템의 깨진 dotnet보다 우선
  if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$HOME/.dotnet:$PATH"
  fi

  command -v dotnet >/dev/null 2>&1 \
    || die ".NET SDK가 없습니다. 아래로 .NET 8을 설치하세요:
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0"

  local v; v="$(dotnet --version 2>/dev/null || true)"
  if [[ -z "$v" ]]; then
    die "dotnet 실행에 실패했습니다(설치 손상 가능). 홈에 .NET 8을 재설치하세요:
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0"
  fi
  c_info ".NET SDK: $v"
  case "$v" in
    8.*) ;;
    *) c_err "경고: .NET 8.0.x 권장 (현재 $v). 계속 진행합니다." ;;
  esac
}

# ── Godot 실행 파일 탐색 ─────────────────────────────────────
find_godot() {
  local g=""
  if [[ -n "${GODOT:-}" ]]; then
    g="$GODOT"
  elif command -v godot >/dev/null 2>&1; then
    g="$(command -v godot)"
  elif command -v godot4 >/dev/null 2>&1; then
    g="$(command -v godot4)"
  else
    # 표준 설치 위치 자동 탐색 (OS별)
    local cand
    if [[ "$OS" == "Darwin" ]]; then
      # macOS: Godot .NET(mono) 빌드는 .app 번들. 실행 파일은 Contents/MacOS/Godot
      for cand in \
        /Applications/Godot*mono*.app/Contents/MacOS/Godot \
        "$HOME"/Applications/Godot*mono*.app/Contents/MacOS/Godot \
        /Applications/Godot.app/Contents/MacOS/Godot \
        "$HOME"/Applications/Godot.app/Contents/MacOS/Godot \
        "$HOME"/.local/godot/*.app/Contents/MacOS/Godot; do
        [[ -x "$cand" ]] && { g="$cand"; break; }
      done
    else
      # Linux: 홈 디렉터리에 내려받은 Godot 4.3 .NET/Mono 실행 파일
      for cand in \
        "$HOME/.local/godot"/*mono*/Godot*mono*.x86_64 \
        "$HOME/.local/godot"/Godot*mono*.x86_64; do
        [[ -x "$cand" ]] && { g="$cand"; break; }
      done
    fi
  fi
  [[ -n "$g" ]] || die "Godot을 찾을 수 없습니다. GODOT 환경변수로 4.3 .NET/Mono 실행 파일 경로를 지정하세요.
  예) GODOT=/path/to/Godot_v4.3-stable_mono_linux.x86_64 ./run.sh"
  local ver; ver="$("$g" --version 2>/dev/null | head -n1 || true)"
  c_info "Godot: $g ($ver)"
  case "$ver" in
    *mono*) ;;
    *) c_err "경고: .mono(.NET) 빌드가 아닌 것 같습니다. C# 빌드가 안 될 수 있습니다." ;;
  esac
  GODOT_BIN="$g"
}

# ── 동작 분기 ────────────────────────────────────────────────
MODE="run"
case "${1:-}" in
  --test)     MODE="test" ;;
  --editor)   MODE="editor" ;;
  --headless) MODE="headless" ;;
  --smoke)    MODE="smoke" ;;
  -h|--help)
    cat <<'EOF'
Blockfall — 한 번에 실행 스크립트

사용법:
  ./run.sh            코어 빌드 후 게임 실행 (기본)
  ./run.sh --test     코어 유닛 테스트만 실행 (Godot 불필요)
  ./run.sh --editor   실행 대신 Godot 에디터로 열기
  ./run.sh --headless 창 없이 C# 솔루션 빌드만 검증
  ./run.sh --smoke    창 없이 오토플레이 스모크 테스트 (전 화면·게임플레이 버그/레이아웃)

Godot 실행 파일 탐색 순서:
  1) 환경변수 GODOT (예: GODOT=/path/to/Godot_v4.3-stable_mono_linux.x86_64 ./run.sh)
  2) PATH 상의 godot / godot4
EOF
    exit 0 ;;
  "" ) ;;
  *) die "알 수 없는 옵션: $1  (--test | --editor | --headless | --smoke | --help)" ;;
esac

check_dotnet

if [[ "$MODE" == "test" ]]; then
  c_info "코어 유닛 테스트 실행 중…"
  dotnet test "$SLN"
  c_ok "테스트 완료."
  exit 0
fi

# 게임 관련 모드는 Godot 필요
find_godot

# GUI 실행/에디터 모드는 디스플레이가 필수 → 빌드 전에 먼저 확인
if [[ "$MODE" == "run" || "$MODE" == "editor" ]]; then
  require_display
fi

c_info "C# 솔루션 빌드 중…"
"$GODOT_BIN" --headless --path "$GAME_DIR" --build-solutions --quit \
  || die "C# 빌드 실패. 'dotnet build $GAME_DIR/Blockfall.csproj'로 상세 오류를 확인하세요."
c_ok "빌드 성공."

case "$MODE" in
  headless)
    c_ok "헤드리스 빌드 검증 완료 (실행 안 함)."
    ;;
  smoke)
    c_info "헤드리스 오토플레이 스모크 테스트 실행 중… (전 화면 + 게임 한 판)"
    "$GODOT_BIN" --headless --path "$GAME_DIR" --import >/dev/null 2>&1 || true
    # 강제종료/널렌더 백엔드가 뿜는 무해한 종료 노이즈만 걸러내고, 판정은 종료코드로.
    NOISE='PagedAllocator|Unreferenced static string|ObjectDB instances leaked|non-existing signal .draw.|BUG: Unreferenced|EDITOR_GET|shader_parameter'
    set +e
    "$GODOT_BIN" --headless --path "$GAME_DIR" -- --autoplay 2>&1 | grep -viE "$NOISE"
    rc=${PIPESTATUS[0]}
    set -e
    [[ $rc -eq 0 ]] && c_ok "스모크 테스트 통과 (RESULT=PASS)." \
                    || die "스모크 테스트 실패 (RESULT=FAIL) — 위 [autoplay] ✗ 로그 확인."
    ;;
  editor)
    c_info "Godot 에디터로 프로젝트 여는 중…"
    exec "$GODOT_BIN" "${GODOT_DISPLAY_ARGS[@]}" --editor --path "$GAME_DIR"
    ;;
  run)
    c_info "게임 실행 중… (창을 닫으면 종료됩니다)"
    exec "$GODOT_BIN" "${GODOT_DISPLAY_ARGS[@]}" --path "$GAME_DIR"
    ;;
esac
