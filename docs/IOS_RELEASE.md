# Blockfall — iOS(App Store) 출시 가이드

Apple Developer Program 등록을 마친 상태에서 App Store 출시까지 가는 절차입니다.
iOS 빌드는 Apple 정책상 macOS + Xcode 환경이 필수인데, 그 환경을 마련하는 방법이
두 가지입니다:

| | 방법 A ⭐ **Codemagic 클라우드 빌드** | 방법 B 로컬 Mac 빌드 |
|---|---|---|
| Mac 필요 | ❌ 불필요 (클라우드 M2 Mac 사용) | ✅ 본인 Mac 필요 |
| 인증서/프로비저닝 | App Store Connect **API 키만 등록하면 자동 생성** | Xcode 자동 서명 |
| TestFlight 업로드 | 빌드 끝나면 **자동** | Xcode에서 수동 Archive→Upload |
| 비용 | 무료 500분/월 (회당 ~15분 → 월 ~30회) | 무료 |
| 사전 준비 | 프로젝트를 **GitHub에 푸시** 필요 | 저장소를 Mac으로 복사 |

> 참고: Codemagic은 Flutter로 유명하지만 **Flutter 전용이 아닙니다** — 이
> 저장소의 `codemagic.yaml` 커스텀 워크플로가 Godot 4.3(.NET) 빌드 전 과정을
> 클라우드 Mac에서 자동으로 수행합니다.

```
방법 A:  GitHub 푸시 → Codemagic "Start build" 클릭 → (자동) → TestFlight
방법 B:  Mac에서 ./build-ios.sh → Xcode Archive → Upload → TestFlight
공통  :  TestFlight 실기기 테스트 → App Store Connect 메타데이터 → 심사 제출
```

---

## 1. Apple 쪽 준비 (공통, 최초 1회)

1. **Team ID 확인** — <https://developer.apple.com/account> → **Membership
   details** → `Team ID` (대문자/숫자 10자리, 예: `ABCDE12345`).
2. **Bundle ID 결정** — 기본값은 `com.blockfall.game`. 바꾸고 싶으면(예:
   `com.hansol.blockfall`) 지금 정하세요 — **한 번 출시하면 영구히 변경 불가**.
   <https://developer.apple.com/account> → **Identifiers → ＋ → App IDs → App**
   으로 등록해 둡니다 (Capabilities는 기본값 그대로).
3. **App Store Connect에서 앱 생성** — <https://appstoreconnect.apple.com> →
   **My Apps → ＋ → New App**:
   - Platform: iOS / Name: `Blockfall` (전 세계 고유해야 하며, 겹치면
     `Blockfall — Neon Puzzle` 등으로)
   - Bundle ID: 위에서 등록한 값
   - SKU: `blockfall-ios-001` 같은 내부 식별자 (아무거나)

---

## 2. 방법 A ⭐ — Codemagic 클라우드 빌드 (Mac 불필요)

### 2-1. 프로젝트를 GitHub에 올리기 (최초 1회)

Codemagic은 git 저장소에서 코드를 받아갑니다. GitHub **비공개(private)**
저장소면 충분합니다. 이 리눅스 PC에서:

```bash
cd ~/work_space/tetris_game
git init && git add . && git commit -m "Blockfall"
# github.com에서 비어 있는 private 저장소(blockfall)를 만든 뒤:
git remote add origin https://github.com/<내계정>/blockfall.git
git branch -M main && git push -u origin main
```

`.gitignore`가 빌드 산출물(`dist/`, `game/build/`, `game/android/` 등)을
걸러 주므로 푸시 용량은 수 MB 수준입니다.

### 2-2. App Store Connect API 키 만들기 (최초 1회)

이 키 하나로 Codemagic이 인증서·프로비저닝 프로파일 생성과 TestFlight 업로드를
전부 대신합니다.

1. <https://appstoreconnect.apple.com> → **Users and Access → Integrations →
   App Store Connect API → Team Keys → ＋**
2. Name: `codemagic`, Access: **App Manager**
3. 생성 후 **API 키 파일(.p8) 다운로드** (한 번만 받을 수 있으니 보관),
   화면의 **Issuer ID**와 **Key ID**를 메모.

### 2-3. Codemagic 설정 (최초 1회)

1. <https://codemagic.io> 가입 (GitHub 계정으로 로그인하면 저장소 연결이 쉬움).
2. **Teams → Personal Account → Integrations → Developer Portal → Manage keys**
   에 2-2에서 만든 키 등록: App Store Connect API key name을 **`blockfall_asc`**
   로 지정하고 Issuer ID / Key ID / .p8 파일 입력.
   (다른 이름을 쓰면 `codemagic.yaml`의 `integrations.app_store_connect` 값을
   같은 이름으로 수정.)
3. **Apps → Add application** → GitHub 저장소 `blockfall` 선택 →
   프로젝트 타입은 **Other** 선택 → 저장소 루트의 `codemagic.yaml`을 자동 인식.

### 2-4. codemagic.yaml에 내 값 채우기

저장소 루트 `codemagic.yaml`에서 두 곳만 확인:

- `TEAM_ID: "XXXXXXXXXX"` → 본인 Team ID로 교체
- Bundle ID를 바꿨다면 `BUNDLE_ID`와 `ios_signing.bundle_identifier`를
  **같은 값으로 함께** 교체

수정 후 커밋·푸시:

```bash
git add codemagic.yaml && git commit -m "team id" && git push
```

### 2-5. 빌드 시작

Codemagic 앱 페이지에서 **Start new build → ios-appstore** 선택 → 시작.
첫 빌드는 약 25분(Godot·템플릿 다운로드 포함), 이후는 캐시 덕에 ~15분.
빌드가 끝나면 **자동으로 TestFlight에 업로드**됩니다 → 4장으로.

빌드 번호는 Codemagic이 회차마다 자동 증가시키므로(`CFBundleVersion`)
"이미 존재하는 빌드 번호" 오류 걱정이 없습니다.

---

## 3. 방법 B — 로컬 Mac 빌드

### 3-1. Mac에 설치할 것 (최초 1회)

| 도구 | 설치 방법 |
|------|-----------|
| **Xcode** (최신) | Mac App Store에서 설치 후 한 번 실행(라이선스 동의). 터미널에서 `xcode-select --install`도 실행 |
| **.NET 8 SDK** | <https://dotnet.microsoft.com/download/dotnet/8.0> — `dotnet --version`이 `8.0.x` |
| **Godot 4.3 (.NET)** | <https://godotengine.org/download/archive/4.3-stable/> 에서 **macOS — .NET** 버전(`Godot_mono.app`)을 `/Applications`에. ⚠ 반드시 ".NET" 표기 빌드 |
| **저장소** | 이 `tetris_game/` 폴더 전체를 Mac으로 복사 (git, AirDrop, USB 등) |

내보내기 템플릿(약 1GB)은 `build-ios.sh`가 최초 1회 자동으로 내려받습니다.
Xcode → Settings → Accounts에 Apple ID(개발자 계정)를 로그인해 두세요 —
인증서·프로파일은 **자동 서명**이 전부 처리하므로 직접 만들 필요 없습니다.

### 3-2. 빌드 (Mac 터미널)

```bash
cd tetris_game
./build-ios.sh --team ABCDE12345 --open        # Team ID는 본인 것으로
# Bundle ID도 바꾼다면:
./build-ios.sh --team ABCDE12345 --bundle com.hansol.blockfall --open
```

환경 점검 → 템플릿 설치 → Team/Bundle ID 주입 → Godot 내보내기(C# AOT 포함,
수 분) → `game/build/ios/`에 **Xcode 프로젝트** 생성 → (`--open`) Xcode 열기.

### 3-3. Xcode에서 업로드

1. 프로젝트 → **Signing & Capabilities** → **Automatically manage signing**
   체크 → **Team** 선택.
2. 실행 대상을 **Any iOS Device (arm64)** 로.
3. **Product → Archive** → Organizer에서 **Distribute App → App Store Connect
   → Upload** → 기본값으로 계속.
4. 10~30분 뒤 App Store Connect의 **TestFlight** 탭에 빌드가 나타납니다.

> 업데이트 배포 때는 `game/export_presets.cfg`의 `application/version`
> (빌드 번호)을 반드시 올려야 합니다 — 방법 A는 자동, 방법 B는 수동.

### 3-4. (선택) 커맨드라인으로 업로드 — App Store Connect API 키 사용

Xcode GUI 의 Distribute App 클릭 대신, **API 키**로 바로 TestFlight 에 올릴 수 있습니다.
자격증명은 저장소 루트 **`ios/`** 폴더에 둡니다 (자세히: [`ios/README.md`](../ios/README.md)):

1. `ios/appstore_connect.env.example` → `ios/appstore_connect.env` 로 복사한 뒤
   **Issuer ID / Key ID / Team ID / Bundle ID** 를 채웁니다.
   (값 만드는 법은 위 **2-2** 와 동일 — App Store Connect API 키 1개면 됩니다.)
2. 다운로드한 `AuthKey_<KeyID>.p8` 를 `ios/private_keys/` 에 저장합니다.
3. `.ipa` 를 만든 뒤(Xcode Organizer 의 *Distribute → Export*, 또는
   `xcodebuild -exportArchive`) 업로드:
   ```bash
   ./ios/upload.sh game/build/ios/Blockfall.ipa
   ```

> - `ios/appstore_connect.env` 와 `.p8` 는 `.gitignore` 로 커밋이 막혀 있습니다 —
>   절대 저장소에 올리지 마세요. 이 폴더는 `game/`(=`res://`) **밖**이라 앱 번들에도
>   포함되지 않습니다.
> - `build-ios.sh` 는 이 파일의 **Team ID / Bundle ID** 를 자동으로 읽으므로, 로컬
>   빌드 때 `--team` 을 매번 주지 않아도 됩니다.
> - **방법 A(Codemagic)** 는 이 파일이 필요 없습니다 — 같은 키를 Codemagic 볼트
>   (`integrations.app_store_connect: blockfall_asc`)에서 관리합니다.

---

## 4. TestFlight로 실기기 테스트 (공통)

App Store Connect → TestFlight → Internal Testing 그룹에 본인 Apple ID를
테스터로 추가 → 아이폰에 **TestFlight 앱** 설치 → 초대 수락 → 설치/실행.
메뉴·게임플레이·터치 조작(드래그로 블록 끼워 넣기)·설정의 스킨 변경까지 한
바퀴 확인하세요.

> 수출 규정 질문("Missing Compliance")은 뜨지 않는 것이 정상 —
> `ITSAppUsesNonExemptEncryption=false`(표준 HTTPS만 사용)가 이미 프리셋의
> Info.plist에 포함되어 있습니다.

## 5. 심사 제출 (공통)

App Store Connect → App Store 탭에서 작성:

- **스크린샷**: 6.9" iPhone(1320×2868) 세트 필수. iPad도 지원되므로 13" iPad
  (2064×2752) 세트도 필요. TestFlight 설치 기기에서 캡처하면 됩니다.
- **설명/키워드/지원 URL**: 자유 작성. ⚠ "Tetris"라는 단어는 상표이므로
  설명·키워드 어디에도 쓰지 마세요 (docs/STORE_SUBMISSION.md 참고).
- **개인정보처리방침 URL**: 필수. 이 게임은 계정·추적·광고 SDK가 없으므로
  "수집하는 데이터 없음" 한 페이지면 충분 (GitHub Pages 등 무료 호스팅 OK).
- **App Privacy 설문**: **Data Not Collected** — 현재 빌드에는 광고/분석/
  로그인이 전혀 없으므로 사실 그대로입니다.
- **연령 등급**: 설문 전부 "아니오" → 4+. / **가격**: 무료.
- **심사 노트**: 온라인 대전(Quick Match/직접 연결)은 부가 기능이며 상대가
  없으면 대기 후 취소된다는 점을 한 줄 적어두면 심사가 매끄럽습니다.

제출 후 심사는 보통 1~3일. 거절되면 사유가 오고, 수정 후 재제출하면 됩니다.

## 6. 이 빌드에 결제·광고가 없는 이유 (중요)

`MobilePlatform`은 광고(AdMob)·인앱결제(StoreKit) **네이티브 플러그인이 실제로
설치돼 있을 때만** `SupportsAds/SupportsIap=true`가 됩니다. 플러그인이 없는
현재 iOS 빌드에서는:

- 스토어에 **무료 스킨만** 표시되고 가격표 붙은 항목(유료 테마·부스터·광고
  제거)은 자동으로 숨습니다 → 결제 없이 지급되는 가짜 구매 버튼이 없으므로
  **심사 지침 3.1.1 위반이 아님**.
- "광고 보고 부활" 버튼도 자동으로 숨습니다 (죽은 버튼 없음).

나중에 IAP/광고를 붙이려면 godot-ios-plugins의 `InAppStore`·AdMob iOS
플러그인을 설치하고 App Store Connect에 인앱 상품을 등록하면 UI가 자동으로
다시 나타납니다 (docs/MONETIZATION.md, docs/BUILD.md §5.3).

## 7. 업데이트 배포

1. 코드 수정 → (방법 A) `git push` 후 Start new build / (방법 B) 재빌드+Archive.
   사용자에게 보이는 버전을 올리려면 `game/export_presets.cfg`의
   `application/short_version`도 수정 (예: `1.0.1`).
2. App Store Connect에서 새 버전 생성 → 새 빌드 선택 → 제출.

## 8. 문제 해결

| 증상 | 조치 |
|------|------|
| Codemagic: 서명 단계 실패 | Developer Portal 통합 키 이름이 `blockfall_asc`인지, API 키 Access가 App Manager인지, Bundle ID가 Identifiers에 등록됐는지 확인 |
| Codemagic: TestFlight 업로드 실패 | App Store Connect에 앱 레코드(1장 3번)가 먼저 만들어져 있어야 합니다 |
| Codemagic: 분(minutes) 부족 | 무료 500분/월 — 캐시가 잡히면 회당 ~15분. 필요 시에만 빌드 |
| `dotnet publish` / AOT 오류 | `dotnet --version`이 8.0.x인지, Xcode 명령줄 도구(`xcode-select -p`) 확인 |
| Godot이 ".NET 빌드 아님" | 표준 Godot 설치함 — 반드시 **.NET(mono)** 빌드 필요 |
| Xcode "No signing team" (방법 B) | Xcode → Settings → Accounts에 Apple ID 로그인 후 Signing 탭에서 팀 선택 |
| Archive 메뉴 비활성화 (방법 B) | 실행 대상을 시뮬레이터가 아닌 **Any iOS Device (arm64)** 로 |
| 업로드 후 빌드가 안 보임 | 처리 중(최대 1시간). App Store Connect 이메일로 리젝 사유 확인 |
| 실기기 크래시 | Godot 4.3 C# iOS는 experimental — Xcode Devices 로그 확인. TestFlight 테스트 필수 |
