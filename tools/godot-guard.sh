#!/usr/bin/env bash
#
# Blockfall — 헤드리스 Godot 호출 공용 방어 래퍼 (타임아웃 + 재시도 + 명시적 실패).
#
#   사용법 (호출자에서 source 후 함수 호출):
#       . "$ROOT/tools/godot-guard.sh"
#       godot_import          "$GODOT_BIN" "$GAME_DIR" || die "리소스 임포트 실패"
#       godot_build_solutions "$GODOT_BIN" "$GAME_DIR" || die "C# 빌드 실패"
#
# ─────────────────────────────────────────────────────────────────────────────
# 왜 이 파일이 존재하는가 (2026-07-27 실제 배포 사고)
# ─────────────────────────────────────────────────────────────────────────────
#   v1.4.2 태그의 Codemagic ios-testflight 1차 빌드가 build-ios.sh 안의
#       "$GODOT" --headless --path game --import >/dev/null 2>&1 || true
#   에서 행(hang)에 걸려 max_build_duration 90분을 전부 태우고 timeout 으로 죽었다.
#   스텝 로그의 마지막 줄은 "[i] 리소스 임포트 중…" 이고 그 뒤 85분간 무출력.
#   서명·.ipa·TestFlight publishing 단계는 실행조차 못 했다 — 태그를 붙였는데 배포가
#   통째로 누락됐다. 동일 태그 재트리거는 같은 단계를 1분 안에 통과하고 4분 만에 완주.
#
#   기존 `|| true` 의 두 가지 결함:
#     1) 행이면 무한정 기다린다 (상위 타임아웃까지 전부 소각).
#     2) 실패해도 조용히 흘러가 다음 단계에서 엉뚱한 오류로 터진다.
#   → 타임아웃 + 재시도 + (CI에서는) 명시적 실패로 대체한다.
#     90분을 태우고 배포가 누락되는 것보다 5분 만에 빨간불이 훨씬 낫다.
#
# ─────────────────────────────────────────────────────────────────────────────
# 2026-07-28 리눅스 실측으로 밝혀진 것 — "간헐 결함"은 두 종류다
# ─────────────────────────────────────────────────────────────────────────────
#   (A) --import 의 SIGABRT: 무작위가 아니라 .NET hostfxr 해석 실패의 결정론적 결과.
#       격리 사본에서 콜드 임포트를 10회씩 두 조건으로 측정:
#           DOTNET_ROOT=$HOME/.dotnet  →  0/10 실패
#           시스템 /usr/bin/dotnet 만  → 10/10 실패 (전부 rc=134 SIGABRT)
#       시스템 dotnet 은 `[/usr/lib/dotnet/host/fxr] does not exist` 로 깨져 있다(CLAUDE.md §5).
#       Godot 은 gd_mono.cpp:399 "Failed to load hostfxr" 후 signal 11 로 죽는다.
#       → _gi_preflight 가 임포트 전에 잡아, 정체불명의 세그폴트를 이름 붙은 오류로 바꾼다.
#         재시도로 절대 낫지 않으므로 로컬/CI 구분 없이 즉시 실패시킨다.
#
#   (B) --build-solutions 의 종료 시점 SIGSEGV: 이건 진짜 간헐이고 무해하다.
#       "Assembly load context unloaded successfully" 를 찍은 뒤(=빌드 성공 후)
#       셧다운 중에 죽는다. 실측 7회 중 1회 발생, 직후 6/6 정상, dotnet build 는 오류 0개.
#       → 재시도하고, 그래도 실패하면 dotnet build 로 진짜 컴파일 결과를 확인한다.
#         Godot 셧다운 크래시가 그린 게이트를 빨간불로 만들면 안 된다.
#
#   그리고 (A)의 크래시 핸들러는 OS 대화상자를 띄우려 한다 — 리눅스에서 zenity 가
#   포털 타임아웃으로 블록되는 것을 실제로 관측했다. 비대화형 CI VM 에서 이 대화상자는
#   영원히 블록될 수 있고, 그것이 바로 사고 당시 85분 무출력의 모습이다.
#   → 자식 프로세스의 stdin 을 /dev/null 로 막고, 워치독으로 상한을 건다.
#
#   이식성: macOS(Codemagic mac_mini_m2)에는 GNU `timeout` 이 없다 →
#           coreutils 의존 없이 순수 bash 워치독으로 구현한다.
#
#   튜닝 환경변수:
#     BLOCKFALL_IMPORT_TIMEOUT   시도당 상한(초), 기본 300
#     BLOCKFALL_IMPORT_ATTEMPTS  최대 시도 횟수,  기본 3
#     BLOCKFALL_IMPORT_STRICT    1=전 시도 실패 시 실패, 0=경고 후 진행.
#                                미지정이면 CI 환경변수 유무로 판정
#                                (Codemagic·GitHub Actions 모두 CI=true 를 세팅).
#
# shellcheck shell=bash

# 종료코드 124 = 워치독 타임아웃 (GNU timeout 관례와 동일).
_GI_RC_TIMEOUT=124
_GI_LAST_RC=0

_gi_log()  { printf '\033[1;36m[godot-guard]\033[0m %s\n' "$*"; }
_gi_warn() { printf '\033[1;33m[godot-guard !]\033[0m %s\n' "$*" >&2; }
_gi_err()  { printf '\033[1;31m[godot-guard ✗]\033[0m %s\n' "$*" >&2; }

_gi_rc_desc() {
  case "$1" in
    0)                 echo "정상" ;;
    "$_GI_RC_TIMEOUT") echo "워치독 타임아웃(행)" ;;
    134)               echo "SIGABRT(중단)" ;;
    139)               echo "SIGSEGV(세그폴트)" ;;
    143)               echo "SIGTERM" ;;
    *)                 echo "종료코드 $1" ;;
  esac
}

# _gi_strict — 이번 실행이 엄격 모드인지 (1/0) 판정해 출력.
_gi_strict() {
  if [ -n "${BLOCKFALL_IMPORT_STRICT:-}" ]; then echo "${BLOCKFALL_IMPORT_STRICT}"; return; fi
  if [ -n "${CI:-}" ]; then echo 1; else echo 0; fi
}

# _gi_attempt <timeout_secs> <bin> <args...>
#   백그라운드 실행 + 폴링 워치독. 타임아웃이면 124, 아니면 자식 종료코드.
_gi_attempt() {
  local secs="$1" bin="$2"; shift 2
  local pid waited=0 grace=0

  # stdin 을 막는다 — 크래시/오류 시 Godot 이 띄우려는 대화상자나 프롬프트가
  # 비대화형 CI 에서 영원히 블록되는 것을 방지(사고의 85분 무출력 메커니즘).
  "$bin" "$@" >/dev/null 2>&1 </dev/null &
  pid=$!

  while kill -0 "$pid" 2>/dev/null; do
    if [ "$waited" -ge "$secs" ]; then
      _gi_warn "${secs}초 초과 — 프로세스(pid $pid)를 강제 종료합니다."
      kill -TERM "$pid" 2>/dev/null
      while kill -0 "$pid" 2>/dev/null && [ "$grace" -lt 5 ]; do
        sleep 1; grace=$((grace + 1))
      done
      kill -KILL "$pid" 2>/dev/null
      wait "$pid" 2>/dev/null
      return "$_GI_RC_TIMEOUT"
    fi
    sleep 1
    waited=$((waited + 1))
    # 30초마다 하트비트 — CI 로그의 무출력 침묵을 없앤다.
    if [ $((waited % 30)) -eq 0 ]; then
      _gi_log "진행 중… ${waited}s / ${secs}s"
    fi
  done

  wait "$pid"
  return $?
}

# _gi_retry <label> <bin> <args...>
#   타임아웃 워치독 + 재시도. 마지막 종료코드를 _GI_LAST_RC 에 남긴다.
_gi_retry() {
  local label="$1" bin="$2"; shift 2
  local secs="${BLOCKFALL_IMPORT_TIMEOUT:-300}"
  local tries="${BLOCKFALL_IMPORT_ATTEMPTS:-3}"
  local i rc=0

  for i in $(seq 1 "$tries"); do
    _gi_log "${label}… (시도 $i/$tries, 타임아웃 ${secs}s)"
    _gi_attempt "$secs" "$bin" "$@"
    rc=$?
    if [ "$rc" -eq 0 ]; then
      [ "$i" -gt 1 ] && _gi_log "재시도 $i 회차에서 성공 — 간헐 결함으로 기록."
      _GI_LAST_RC=0
      return 0
    fi
    _gi_warn "${label} 시도 $i/$tries 실패: $(_gi_rc_desc "$rc")"
    [ "$i" -lt "$tries" ] && sleep 3
  done

  _GI_LAST_RC="$rc"
  return 1
}

# .NET 사전 점검. Godot mono 빌드는 hostfxr 를 로드하는데, 실패하면 진단 메시지 없이
# SIGABRT 로 죽는다(실측 10/10). 재시도해도 절대 낫지 않는 결정론적 실패이므로
# 여기서 즉시 잡아 이름 붙은 오류로 돌려준다.
_gi_preflight() {
  if ! command -v dotnet >/dev/null 2>&1; then
    _gi_err ".NET SDK 를 찾을 수 없습니다 — Godot mono 는 hostfxr 없이 SIGABRT 로 죽습니다."
    _gi_err '해결: export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"'
    return 1
  fi
  if ! dotnet --version >/dev/null 2>&1; then
    _gi_err "dotnet 은 PATH 에 있으나 동작하지 않습니다: $(command -v dotnet)"
    _gi_err "$(dotnet --version 2>&1 | head -n1)"
    _gi_err "이 환경의 시스템 dotnet 은 fxr 누락으로 깨져 있습니다(CLAUDE.md §5)."
    _gi_err '해결: export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"'
    return 1
  fi
  return 0
}

# ── 공개 함수 ────────────────────────────────────────────────────────────────

# godot_import <godot_bin> <game_dir>
#   성공/관대 → 0, 실패(엄격) → 1
godot_import() {
  local bin="$1" dir="$2"

  # 사전 점검 실패는 재시도로 낫지 않고, 그냥 진행하면 정체불명의 SIGABRT 가 된다.
  # 로컬/CI 구분 없이 즉시 실패시킨다 (관대 모드보다 이쪽이 항상 덜 나쁘다).
  _gi_preflight || return 1

  if _gi_retry "리소스 임포트" "$bin" --headless --path "$dir" --import; then
    return 0
  fi

  if [ "$(_gi_strict)" = "1" ]; then
    _gi_err "리소스 임포트가 모든 시도에서 실패했습니다 (마지막: $(_gi_rc_desc "$_GI_LAST_RC"))."
    _gi_err "CI 모드이므로 여기서 중단합니다 — 뒤 단계(서명/내보내기)를 침묵 속에 태우지 않습니다."
    return 1
  fi

  _gi_warn "리소스 임포트가 모든 시도에서 실패했습니다 (마지막: $(_gi_rc_desc "$_GI_LAST_RC")) — 로컬 모드라 계속 진행합니다."
  _gi_warn "내보내기가 리소스 누락으로 실패하면 이 줄을 먼저 의심하세요."
  return 0
}

# godot_build_solutions <godot_bin> <game_dir>
#   성공 → 0, 진짜 컴파일 오류 → 1
#
#   Godot 은 C# 빌드를 성공적으로 끝낸 뒤 셧다운에서 간헐적으로 SIGSEGV 로 죽는다
#   (실측 7회 중 1회). 종료코드만 보면 그린 빌드가 빨간불이 된다 — 그래서 재시도하고,
#   그래도 실패하면 dotnet build 로 진짜 컴파일 결과를 확인해 판정한다.
#   판정 권위는 컴파일러이지 Godot 의 셧다운 경로가 아니다.
godot_build_solutions() {
  local bin="$1" dir="$2"

  _gi_preflight || return 1

  if _gi_retry "C# 솔루션 빌드" "$bin" --headless --path "$dir" --build-solutions --quit; then
    return 0
  fi

  _gi_warn "Godot --build-solutions 가 모든 시도에서 실패했습니다 (마지막: $(_gi_rc_desc "$_GI_LAST_RC"))."
  _gi_warn "셧다운 크래시와 진짜 컴파일 오류를 구분하기 위해 dotnet build 로 재확인합니다…"
  if dotnet build "$dir/Blockfall.csproj" --nologo -v q >/dev/null 2>&1 </dev/null; then
    _gi_warn "dotnet build 는 성공 — Godot 셧다운 크래시로 판단하고 통과 처리합니다."
    return 0
  fi

  _gi_err "dotnet build 도 실패 — 진짜 컴파일 오류입니다."
  _gi_err "상세: dotnet build $dir/Blockfall.csproj"
  return 1
}
