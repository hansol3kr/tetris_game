#!/usr/bin/env bash
#
# Blockfall — App Store Connect API 키 "연동 확인".
#
#   ios/appstore_connect.env 의 Issuer ID / Key ID / .p8 로 ES256 JWT 를 서명해
#   Apple 공식 API(GET /v1/apps)에 인증 요청을 보냅니다. 자격증명이 유효한지,
#   그리고 이 키로 어떤 앱이 보이는지(= 어느 팀/계정 키인지) 알려줍니다.
#   macOS/Linux 어디서나 동작합니다 (Xcode 불필요, PyJWT 만 필요).
#
# 사용법:  ./ios/verify.sh
#
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/ios/appstore_connect.env"

c_err(){ printf '\033[1;31m[✗]\033[0m %s\n' "$*" >&2; }
die(){ c_err "$*"; exit 1; }

[[ -f "$ENV_FILE" ]] || die "ios/appstore_connect.env 없음 — ios/appstore_connect.env.example 복사 후 값 채우기 (ios/README.md)."
# shellcheck disable=SC1090
source "$ENV_FILE"
: "${ASC_ISSUER_ID:?ASC_ISSUER_ID 가 비어 있습니다}"
: "${ASC_KEY_ID:?ASC_KEY_ID 가 비어 있습니다}"

# 개인키 경로: 절대경로면 그대로, 아니면 저장소 루트 기준.
KEY="${ASC_PRIVATE_KEY_PATH:-ios/private_keys/AuthKey_${ASC_KEY_ID}.p8}"
[[ "$KEY" = /* ]] || KEY="$ROOT/$KEY"
[[ -f "$KEY" ]] || die "개인키(.p8)를 찾을 수 없습니다: $KEY"

# Issuer ID 형식 경고 (Team 키의 Issuer ID 는 UUID; Key ID 를 잘못 넣는 실수가 흔함).
if [[ ! "$ASC_ISSUER_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
  c_err "경고: ASC_ISSUER_ID('$ASC_ISSUER_ID') 가 UUID 형식이 아닙니다."
  c_err "  Issuer ID 는 App Store Connect > Users and Access > Integrations >"
  c_err "  App Store Connect API 페이지 상단의 UUID 입니다 (Key ID 와 다름)."
fi

command -v python3 >/dev/null 2>&1 || die "python3 필요."
python3 - "$ASC_ISSUER_ID" "$ASC_KEY_ID" "$KEY" <<'PY'
import sys, time, json, urllib.request, urllib.error
try:
    import jwt
except ImportError:
    sys.exit("PyJWT 필요:  python3 -m pip install pyjwt cryptography")
issuer, kid, p8 = sys.argv[1], sys.argv[2], sys.argv[3]
now = int(time.time())
tok = jwt.encode({"iss": issuer, "iat": now, "exp": now + 600, "aud": "appstoreconnect-v1"},
                 open(p8).read(), algorithm="ES256", headers={"kid": kid, "typ": "JWT"})
req = urllib.request.Request("https://api.appstoreconnect.apple.com/v1/apps?limit=50",
                             headers={"Authorization": "Bearer " + tok})
try:
    with urllib.request.urlopen(req, timeout=20) as r:
        code, body = r.status, r.read().decode()
except urllib.error.HTTPError as e:
    code, body = e.code, e.read().decode()
except Exception as e:
    sys.exit(f"요청 실패(네트워크): {e}")
d = json.loads(body)
if code == 200:
    apps = d.get("data", [])
    print(f"[✓] 인증 성공 — 이 API 키로 보이는 앱 {len(apps)}개:")
    for a in apps:
        at = a.get("attributes", {})
        print(f"    - {at.get('name')}  [{at.get('bundleId')}]")
    if not apps:
        print("    (앱 0개 — 키는 유효하나 이 계정에 아직 등록된 앱이 없음)")
else:
    print(f"[✗] 인증 실패 — HTTP {code}")
    for e in d.get("errors", [{}]):
        print(f"    {e.get('status')} {e.get('code')}: {e.get('title')}")
        if e.get('detail'):
            print(f"       {e.get('detail')}")
    sys.exit(1)
PY
