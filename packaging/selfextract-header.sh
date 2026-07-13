#!/usr/bin/env bash
#
# Blockfall — 자기추출 실행 파일 (self-extracting).
# 이 파일 하나만 복사해서 실행하면 됩니다:  ./Blockfall.run
#
# 처음 실행하면 ~/.cache/blockfall/ 에 한 번 풀고, 이후에는 바로 실행합니다.
# 나머지 환경 점검(권한/디스플레이/렌더러 폴백)은 내부 Blockfall.sh 가 처리합니다.
#
set -euo pipefail

MARKER="__BLOCKFALL_ARCHIVE_BELOW__"
BUILD_ID="__BUILD_ID__"
PKG="Blockfall-linux-x86_64"

c_i(){  printf '\033[1;36m[i]\033[0m %s\n' "$*"; }
c_err(){ printf '\033[1;31m[✗]\033[0m %s\n' "$*" >&2; }

CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/blockfall/$BUILD_ID"
LAUNCH="$CACHE/$PKG/Blockfall.sh"

if [[ ! -x "$LAUNCH" ]]; then
  command -v tar >/dev/null 2>&1 || { c_err "tar 가 필요합니다."; exit 1; }
  c_i "처음 실행: 게임을 $CACHE 에 준비하는 중…"
  mkdir -p "$CACHE"
  line="$(awk -v m="$MARKER" '$0==m{print NR+1; exit}' "$0")"
  if [[ -z "${line:-}" ]]; then c_err "아카이브를 찾지 못했습니다 (파일 손상?)."; exit 1; fi
  tail -n +"$line" "$0" | tar xz -C "$CACHE"
  [[ -x "$LAUNCH" ]] || { c_err "압축 해제에 실패했습니다."; exit 1; }
fi

exec "$LAUNCH" "$@"
exit 0
__BLOCKFALL_ARCHIVE_BELOW__
