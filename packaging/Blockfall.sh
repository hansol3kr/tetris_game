#!/usr/bin/env bash
#
# Blockfall 실행 런처 — 어떤 리눅스 데스크톱에서도 최대한 실행되도록
# 환경(권한 / 아키텍처 / 디스플레이 / 렌더러 / 라이브러리)을 스스로
# 점검하고 대응합니다. 어느 폴더에서 실행하든(더블클릭 포함) 안전합니다.
#
# 사용법:
#   ./Blockfall.sh                  게임 실행 (자동: Vulkan → 실패 시 OpenGL 폴백)
#   ./Blockfall.sh --gl             강제로 OpenGL 호환 모드로 실행
#   ./Blockfall.sh --vulkan         강제로 Vulkan(forward_plus)로 실행
#   ./Blockfall.sh --check          실행하지 않고 환경 진단만 출력
#   ./Blockfall.sh --headless-check 창 없이 부팅만 검증 (화면 없는 서버/SSH용)
#   그 밖의 인자(예: --fullscreen)는 그대로 게임(Godot)에 전달됩니다.
#
# 환경변수:
#   BLOCKFALL_DRIVER=gl|vulkan      렌더러 강제 (인자 --gl/--vulkan 과 동일)
#
set -uo pipefail

SELF="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN="$SELF/Blockfall.x86_64"
DATA="$SELF/data_Blockfall_linuxbsd_x86_64"

c_i(){  printf '\033[1;36m[i]\033[0m %s\n' "$*"; }
c_ok(){ printf '\033[1;32m[✓]\033[0m %s\n' "$*"; }
c_err(){ printf '\033[1;31m[✗]\033[0m %s\n' "$*" >&2; }

# ── 배포판별 라이브러리 설치 안내 ────────────────────────────
print_install_hint(){
  local id="" like=""
  if [[ -r /etc/os-release ]]; then
    # shellcheck disable=SC1091
    . /etc/os-release; id="${ID:-}"; like="${ID_LIKE:-}"
  fi
  case " $id $like " in
    *debian*|*ubuntu*|*mint*)
      echo "  설치: sudo apt update && sudo apt install -y \\" >&2
      echo "        libx11-6 libxcursor1 libxinerama1 libxrandr2 libxi6 libxext6 \\" >&2
      echo "        libgl1 libglx-mesa0 libasound2 libudev1 libxkbcommon0 \\" >&2
      echo "        libwayland-client0 libvulkan1 fontconfig" >&2 ;;
    *fedora*|*rhel*|*centos*|*rocky*|*alma*)
      echo "  설치: sudo dnf install -y libX11 libXcursor libXinerama libXrandr libXi \\" >&2
      echo "        libXext mesa-libGL alsa-lib systemd-libs libxkbcommon wayland-libs \\" >&2
      echo "        vulkan-loader fontconfig" >&2 ;;
    *arch*|*manjaro*|*endeavour*)
      echo "  설치: sudo pacman -S --needed libx11 libxcursor libxinerama libxrandr libxi \\" >&2
      echo "        libxext libglvnd alsa-lib systemd libxkbcommon wayland \\" >&2
      echo "        vulkan-icd-loader fontconfig" >&2 ;;
    *)
      echo "  → 위 라이브러리(주로 X11 / OpenGL / ALSA 계열)를 배포판 패키지 관리자로 설치하세요." >&2 ;;
  esac
}

# ── 인자 파싱 ────────────────────────────────────────────────
MODE="run"            # run | check | headless
FORCE=""              # "" | gl | vulkan
PASS=()               # 게임(Godot)에 그대로 넘길 인자
for a in "$@"; do
  case "$a" in
    --gl|--opengl|--opengl3) FORCE="gl" ;;
    --vulkan|--vk)           FORCE="vulkan" ;;
    --check)                 MODE="check" ;;
    --headless-check)        MODE="headless" ;;
    -h|--help)
      grep -E '^#( |$)' "$0" | sed 's/^#\{1,\} \{0,1\}//'; exit 0 ;;
    *) PASS+=("$a") ;;
  esac
done
case "${BLOCKFALL_DRIVER:-}" in
  gl|opengl|opengl3) FORCE="gl" ;;
  vulkan|vk)         FORCE="vulkan" ;;
esac

# ── 1) 파일 / 권한 점검 ──────────────────────────────────────
if [[ ! -f "$BIN" ]]; then
  c_err "게임 본체가 없습니다: $BIN"
  echo "  → 폴더를 통째로(또는 .run 파일을) 복사했는지 확인하세요." >&2
  exit 1
fi
if [[ ! -d "$DATA" ]]; then
  c_err "필수 폴더가 없습니다: $(basename "$DATA")"
  echo "  → Blockfall.x86_64 와 반드시 같은 위치에 있어야 합니다." >&2
  exit 1
fi
[[ -x "$BIN" ]] || { chmod +x "$BIN" 2>/dev/null && c_i "실행 권한을 부여했습니다."; }

# ── 2) 아키텍처 점검 ─────────────────────────────────────────
ARCH="$(uname -m 2>/dev/null || echo unknown)"
case "$ARCH" in
  x86_64|amd64) ;;
  *) c_err "이 빌드는 64비트 x86_64 전용입니다 (현재: $ARCH)."
     echo "  → $ARCH 환경에서는 해당 아키텍처용으로 다시 export 해야 합니다." >&2
     exit 1 ;;
esac

# ── 3) 누락 라이브러리 점검(경고, 중단하지 않음) ─────────────
if command -v ldd >/dev/null 2>&1; then
  # Vulkan 은 런타임 dlopen 이라 ldd 에 안 잡히며, 없어도 OpenGL 로 폴백하므로 제외.
  HARD_MISS="$(ldd "$BIN" 2>/dev/null | awk '/not found/{print $1}' | grep -vi 'vulkan' | sort -u)"
  if [[ -n "$HARD_MISS" ]]; then
    c_err "다음 시스템 라이브러리를 찾을 수 없습니다:"
    printf '%s\n' "$HARD_MISS" | sed 's/^/     /' >&2
    print_install_hint
    c_i "그래도 실행을 시도합니다…"
  fi
fi

# Vulkan 로더 존재 여부(참고용 / 기본 렌더러 선택에 사용)
have_vulkan=1
if command -v ldconfig >/dev/null 2>&1; then
  ldconfig -p 2>/dev/null | grep -qiE 'libvulkan\.so' || have_vulkan=0
fi

# ── 4) 진단 / 헤드리스 모드 ──────────────────────────────────
if [[ "$MODE" == "check" ]]; then
  c_i "환경 진단"
  echo "  실행파일    : $BIN"
  echo "  아키텍처    : $ARCH"
  echo "  DISPLAY     : ${DISPLAY:-<없음>}"
  echo "  WAYLAND     : ${WAYLAND_DISPLAY:-<없음>}"
  echo "  Vulkan 로더 : $([[ $have_vulkan -eq 1 ]] && echo '있음' || echo '없음 → OpenGL 사용')"
  if "$BIN" --headless --quit-after 1 >/dev/null 2>&1; then
    c_ok "부팅 테스트 통과 (엔진 + .NET 어셈블리 정상 로드)"
  else
    c_err "부팅 테스트 실패 — 라이브러리 누락 가능. 위 안내를 확인하세요."
  fi
  exit 0
fi

if [[ "$MODE" == "headless" ]]; then
  c_i "헤드리스 부팅 검증 (창 없음)…"
  exec "$BIN" --headless --quit-after 150 ${PASS[@]+"${PASS[@]}"}
fi

# ── 5) 실제 실행 → 디스플레이 필수 ───────────────────────────
if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
  c_err "그래픽 디스플레이가 없습니다 (DISPLAY / WAYLAND_DISPLAY 미설정)."
  echo "  모니터가 연결된 데스크톱 세션에서 실행하세요. SSH만으로는 창이 뜨지 않습니다." >&2
  echo "  (화면 없이 부팅만 확인:  $0 --headless-check )" >&2
  exit 1
fi

GL_ARGS=(--rendering-method gl_compatibility --rendering-driver opengl3)

if [[ "$FORCE" == "gl" ]]; then
  c_i "OpenGL 호환 모드로 실행합니다."
  exec "$BIN" "${GL_ARGS[@]}" ${PASS[@]+"${PASS[@]}"}
fi

if [[ "$FORCE" != "vulkan" && $have_vulkan -eq 0 ]]; then
  c_i "Vulkan 로더 미탐지 → OpenGL 호환 모드로 실행합니다."
  exec "$BIN" "${GL_ARGS[@]}" ${PASS[@]+"${PASS[@]}"}
fi

# 기본: Vulkan(forward_plus) 먼저 시도, 초기화 실패 시 OpenGL 로 자동 재시도.
c_i "게임을 실행합니다 (Vulkan / forward_plus)…"
start=$SECONDS
"$BIN" ${PASS[@]+"${PASS[@]}"}
code=$?
if [[ "$FORCE" != "vulkan" && $code -ne 0 && $(( SECONDS - start )) -lt 15 ]]; then
  c_err "Vulkan 렌더러 초기화에 실패한 것으로 보입니다 (종료 코드 $code)."
  c_i "OpenGL 호환 모드로 자동 재시도합니다…"
  "$BIN" "${GL_ARGS[@]}" ${PASS[@]+"${PASS[@]}"}
  code=$?
fi
exit $code
