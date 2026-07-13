# Blockfall — 자동 배포(CD) 가이드

릴리스를 **git 태그 하나로** 내보내는 파이프라인입니다.

```
git tag v1.0.1 && git push origin v1.0.1
        │
        ├─→ Codemagic  ios-testflight     → TestFlight 자동 업로드
        ├─→ Codemagic  android-playstore  → Google Play 내부 테스트 트랙 업로드
        └─(수동)       GitHub Actions      → 데스크톱(Linux/Windows/macOS) 빌드
```

버전을 여러 파일에 손으로 고칠 필요가 없습니다 — **태그 이름이 곧 버전**이고,
스토어 빌드 번호는 CI가 자동으로 올립니다.

| 파이프라인 | 도구 | 트리거 | 결과물 | 설정 문서 |
|-----------|------|--------|--------|-----------|
| iOS | Codemagic (`codemagic.yaml`) | 태그 `v*` push | TestFlight | 이 문서 §3 + IOS_RELEASE.md |
| Android | Codemagic (`codemagic.yaml`) | 태그 `v*` push | Play 내부 트랙 | 이 문서 §4 |
| Desktop | GitHub Actions (`.github/workflows/desktop-build.yml`) | 수동 실행 | 빌드 아티팩트 | 이 문서 §5 |

---

## 1. 버전 규칙 (핵심)

- **마케팅 버전**(사용자에게 보이는 `1.0.1`) = git 태그에서 `v`를 뗀 값.
- **빌드 번호**(스토어 내부, 업로드마다 증가) = Codemagic `$BUILD_NUMBER`(iOS
  CFBundleVersion / Android `version/code`). GitHub Actions는 `run_number`.
- 실제로 값을 써넣는 도구는 `tools/set-version.py` 하나입니다. 모든 파이프라인이
  이걸 호출해 `game/export_presets.cfg`의 각 플랫폼 프리셋을 동시에 맞춥니다:

  ```bash
  python3 tools/set-version.py --version 1.0.1 --build 12   # CD가 자동 호출
  python3 tools/set-version.py --version 1.0.1 --print      # 미리보기(안 씀)
  ```

  프리셋의 `1.0.0`은 태그 없이 수동 빌드할 때의 기본값(fallback)일 뿐이니
  그대로 두면 됩니다.

## 2. 릴리스 하는 법 (준비가 끝난 뒤)

```bash
# 1) 변경사항 커밋
git add -A && git commit -m "..."      # 그리고 git push

# 2) 버전 태그를 달고 밀면 끝 — iOS/Android 빌드가 자동으로 시작됩니다
git tag v1.0.1
git push origin v1.0.1
```

Codemagic 대시보드에서 두 워크플로가 도는 것을 볼 수 있고, 끝나면 각각
TestFlight / Play 내부 트랙에 올라옵니다. 실기기 확인 후 각 스토어 콘솔에서
**심사 제출**은 수동으로 합니다(처음엔 자동 제출을 켜지 않는 걸 권장).

> 태그를 잘못 올렸다면: `git tag -d v1.0.1 && git push origin :refs/tags/v1.0.1`
> 로 지우고 (스토어에 이미 업로드됐다면 그 빌드는 콘솔에서 폐기), 다음 번호로
> 다시 태그하세요. 빌드 번호는 어차피 계속 증가하므로 충돌하지 않습니다.

---

## 3. iOS 파이프라인 설정 (최초 1회)

전체 절차는 **`docs/IOS_RELEASE.md` 방법 A**에 있습니다. 요약:

1. 저장소를 GitHub에 push (Codemagic이 여기서 코드를 받아감).
2. App Store Connect **API 키**(Access: App Manager) 발급 → Codemagic
   **Team settings → Integrations → Developer Portal**에 이름 **`blockfall_asc`**
   로 등록.
3. `codemagic.yaml`의 `ios-testflight` 워크플로에서 `TEAM_ID`를 본인 값으로 교체
   (Bundle ID를 바꿨다면 `BUNDLE_ID`와 `ios_signing.bundle_identifier`도 함께).
4. App Store Connect에 앱 레코드 생성.

이제 태그를 밀면 자동으로 TestFlight까지 갑니다. (수동 실행도 언제든 가능:
Codemagic → Start new build → ios-testflight)

## 4. Android 파이프라인 설정 (최초 1회)

> Google Play는 **개발자 등록비 $25(1회)**가 필요합니다. 아래 자격증명이
> 준비되기 전에는 `android-playstore` 워크플로가 서명/업로드 단계에서 실패하니,
> Play 준비 전이라면 태그를 밀 때 iOS만 통과하고 Android는 빨간불이 정상입니다.

### 4-1. 업로드 키스토어 만들기 (한 번, 절대 분실 금지)

```bash
keytool -genkeypair -v -keystore blockfall-upload.jks \
  -alias blockfall -keyalg RSA -keysize 2048 -validity 10000
# 비밀번호(스토어/키)와 alias를 기록해 두세요. 이 파일을 잃어버리면
# 같은 앱을 업데이트할 수 없습니다(Play App Signing 등록 전 기준).
```

Codemagic에 넣기 위해 base64로 인코딩:

```bash
base64 -w0 blockfall-upload.jks > blockfall-upload.jks.b64   # macOS는 -w0 대신 -b0
```

### 4-2. Play 서비스 계정 JSON

<https://play.google.com/console> → 설정 → API 액세스 → 새 서비스 계정 생성 →
Google Cloud에서 JSON 키 발급 → Play Console에서 "앱 릴리스 관리" 권한 부여.
이 JSON 문자열을 통째로 씁니다.

### 4-3. Codemagic 환경변수 그룹 `blockfall_android`

Codemagic 앱 설정 → Environment variables → 그룹 이름 **`blockfall_android`** 에
다음을 **Secure** 로 추가 (`codemagic.yaml`가 이 이름들을 그대로 참조):

| 변수 | 값 |
|------|----|
| `BLOCKFALL_RELEASE_KEYSTORE` | 4-1의 `.jks.b64` 내용 |
| `BLOCKFALL_KEYSTORE_PASSWORD` | 키스토어(store) 비밀번호 |
| `BLOCKFALL_KEY_ALIAS` | `blockfall` (위 alias) |
| `BLOCKFALL_KEY_PASSWORD` | 키(key) 비밀번호 |
| `GCLOUD_SERVICE_ACCOUNT_CREDENTIALS` | 4-2의 서비스 계정 JSON 전체 |

`codemagic.yaml`의 `PACKAGE_NAME`(기본 `com.blockfall.game`)이 Play Console의
패키지명과 같은지 확인하세요. Play Console에는 첫 업로드용 앱을 미리 만들어
두어야 하며(내부 테스트 트랙 활성화), 최초 1회는 콘솔에서 수동으로 .aab를
올려 앱을 "생성 완료" 상태로 만든 뒤부터 이 파이프라인의 자동 업로드가 됩니다.

> 이 워크플로는 헤드리스 CI에서 Godot **안드로이드 빌드 템플릿**(`android_source.zip`)을
> export 템플릿에서 꺼내 설치하고, 에디터 설정에 SDK/JDK 경로를 써넣은 뒤
> `.aab`를 릴리스 서명(환경변수 `GODOT_ANDROID_KEYSTORE_RELEASE_*`)으로
> 내보냅니다. 첫 실행 로그에서 SDK/JDK 경로가 맞는지 한 번 확인하세요 —
> Codemagic 이미지 경로가 다르면 "SDK/JDK 경로 주입" 스텝의 기본값만 고치면 됩니다.

## 5. Desktop 파이프라인 (GitHub Actions, 수동)

데스크톱은 Steam 유료 판매 계획이 있어 **자동 공개 릴리스를 하지 않습니다**.
대신 필요할 때 빌드만 뽑습니다:

GitHub → **Actions → Desktop build → Run workflow** → (선택) 버전 입력 → 실행.
Linux/Windows/macOS 산출물이 워크플로 **아티팩트**로 올라옵니다(공개 아님, 14일 보관).
로컬에서는 `./build-all.sh linux windows macos` 로 동일하게 만들 수 있습니다.

## 6. (선택) 확장 아이디어

- **Steam 자동 업로드**: `steamcmd`로 depot 업로드하는 스텝을 GitHub Actions에
  추가할 수 있습니다. Steam 파트너 계정 + App ID + depot 설정 + 빌드 계정
  자격증명(2FA 우회용 `config.vdf`)이 필요합니다. Steam 준비가 되면 이 절에
  워크플로를 붙이세요. (docs/BUILD.md §5.1의 GodotSteam 설정 선행)
- **심사 자동 제출**: iOS는 `codemagic.yaml`의 `submit_to_app_store`, Android는
  `track: production`으로 바꾸면 심사까지 자동화됩니다. 안정화 전에는 수동 제출
  권장(리뷰 리젝 시 대응 여유).
- **베타 채널 분리**: `main` 브랜치 push → 내부 테스터, 태그 → 정식 후보처럼
  워크플로를 나눌 수 있습니다(현재는 태그 단일 채널로 단순화).

## 7. 첫 릴리스 체크리스트

- [ ] 저장소 GitHub에 push, Codemagic 앱 연결됨
- [ ] iOS: `blockfall_asc` API 키 등록, `TEAM_ID` 채움, ASC 앱 레코드 생성 (§3)
- [ ] Android: `blockfall_android` 그룹 5개 변수, Play 앱 첫 .aab 수동 업로드 (§4)
- [ ] `codemagic.yaml`의 `TEAM_ID`/`BUNDLE_ID`/`PACKAGE_NAME` 확인
- [ ] `git tag v1.0.0 && git push origin v1.0.0` → 두 워크플로 그린라이트 확인
- [ ] TestFlight/Play 내부 트랙에서 실기기 테스트 (Godot C# iOS는 experimental)
- [ ] 각 스토어 콘솔에서 메타데이터 작성 후 **수동 심사 제출**

## 8. 문제 해결

| 증상 | 원인/조치 |
|------|-----------|
| 태그를 밀었는데 빌드가 안 뜸 | Codemagic에 저장소가 연결됐고 웹훅이 등록됐는지 확인. 태그 형식이 `v1.0.1`(= `v*.*.*`)인지 |
| iOS 서명 실패 | `blockfall_asc` 통합 키가 App Manager 권한인지, `TEAM_ID`가 맞는지, Bundle ID가 ASC에 등록됐는지 |
| Android "keystore 검증 실패" | `BLOCKFALL_KEY_ALIAS`/비밀번호가 4-1에서 만든 값과 일치하는지, base64가 한 줄(`-w0`)인지 |
| Android SDK/JDK 못 찾음 | "SDK/JDK 경로 주입" 스텝 로그의 경로 확인 → 기본값을 Codemagic 이미지 실제 경로로 수정 |
| Play 업로드 거절 (앱 없음) | Play Console에서 최초 1회 수동으로 .aab를 올려 앱을 만든 뒤부터 자동 업로드 가능 |
| 버전이 안 바뀜 | 태그 없이 수동 빌드하면 프리셋 기본값(1.0.0) 사용 — 버전을 바꾸려면 태그로 빌드하거나 `--version` 전달 |
| 빌드 번호 충돌 | `$BUILD_NUMBER`는 프로젝트 전역 증가라 충돌하지 않음. 스토어가 "이미 존재하는 빌드"라 하면 이전 업로드 실패분 — 다음 빌드가 자동 해결 |
