#!/usr/bin/env bash
#
# Blockfall — App Store Connect(TestFlight) 로컬 업로드 (macOS 전용).
#
#   ios/appstore_connect.env 의 App Store Connect API 키로 .ipa 를 업로드합니다.
#   Xcode GUI 의 "Distribute App → Upload" 클릭 과정을 커맨드라인 한 줄로 대체.
#
# 사용법 (Mac 터미널):
#   ./ios/upload.sh game/build/ios/Blockfall.ipa
#
# 준비물 (ios/README.md 참고):
#   1) ios/appstore_connect.env            (ios/appstore_connect.env.example 복사 후 값 채움)
#   2) ios/private_keys/AuthKey_<KeyID>.p8  (App Store Connect 에서 받은 개인키)
#   3) Xcode Command Line Tools            (xcrun altool)
#
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/ios/appstore_connect.env"

c_i(){  printf '\033[1;36m[i]\033[0m %s\n' "$*"; }
c_ok(){ printf '\033[1;32m[✓]\033[0m %s\n' "$*"; }
c_err(){ printf '\033[1;31m[✗]\033[0m %s\n' "$*" >&2; }
die(){ c_err "$*"; exit 1; }

# ── .ipa 인자 ────────────────────────────────────────────────
IPA="${1:-}"
[[ -n "$IPA" ]]  || die "사용법: ./ios/upload.sh <path-to-.ipa>"
[[ -f "$IPA" ]]  || die ".ipa 파일을 찾을 수 없습니다: $IPA"
IPA="$(cd "$(dirname "$IPA")" && pwd)/$(basename "$IPA")"  # 절대경로로 고정

# ── 환경 점검 ────────────────────────────────────────────────
[[ "$(uname -s)" == "Darwin" ]] || die "업로드는 macOS 에서만 됩니다 (xcrun altool 필요). 현재: $(uname -s)."
command -v xcrun >/dev/null 2>&1 || die "xcrun 없음 — Xcode Command Line Tools 설치: xcode-select --install"

# ── 자격증명 로드 ────────────────────────────────────────────
[[ -f "$ENV_FILE" ]] || die "자격증명 파일이 없습니다: ios/appstore_connect.env
  → ios/appstore_connect.env.example 를 복사해 값을 채우세요 (ios/README.md 참고)."
# shellcheck disable=SC1090
source "$ENV_FILE"

[[ -n "${ASC_ISSUER_ID:-}" ]] || die "ASC_ISSUER_ID 가 비어 있습니다 (ios/appstore_connect.env)."
[[ -n "${ASC_KEY_ID:-}"    ]] || die "ASC_KEY_ID 가 비어 있습니다 (ios/appstore_connect.env)."

KEY_PATH="$ROOT/${ASC_PRIVATE_KEY_PATH:-ios/private_keys/AuthKey_${ASC_KEY_ID}.p8}"
[[ -f "$KEY_PATH" ]] || die "개인키(.p8)를 찾을 수 없습니다: $KEY_PATH
  → App Store Connect 에서 받은 AuthKey_<KeyID>.p8 를 ios/private_keys/ 에 두세요."

# altool 은 ~/.appstoreconnect/private_keys 등 표준 위치에서 AuthKey_<KeyID>.p8 을
# 자동으로 찾는다 — 확실히 그 위치에 두고 실행.
mkdir -p "$HOME/.appstoreconnect/private_keys"
cp -f "$KEY_PATH" "$HOME/.appstoreconnect/private_keys/AuthKey_${ASC_KEY_ID}.p8"

# ── 업로드 ───────────────────────────────────────────────────
c_i "TestFlight 업로드 중: $(basename "$IPA")  (issuer ${ASC_ISSUER_ID:0:8}…, key ${ASC_KEY_ID})"
xcrun altool --upload-app --type ios --file "$IPA" \
  --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID" \
  || die "업로드 실패 — 위 altool 로그 확인 (빌드번호 중복 / 서명 / 자격 등). docs/IOS_RELEASE.md 8장 참고."

c_ok "업로드 완료 — App Store Connect ▸ TestFlight 에서 처리 상태를 확인하세요 (10~30분)."
