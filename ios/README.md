# ios/ — Apple 배포 자격증명 (로컬)

Blockfall 을 **로컬 Mac 에서** App Store Connect(TestFlight)로 커맨드라인 업로드할 때
쓰는 **App Store Connect API 키**를 두는 곳입니다. (Xcode GUI 의 Distribute App
클릭 과정을 `ios/upload.sh` 한 줄로 대체합니다.)

## 넣는 법 (3단계)
1. `appstore_connect.env.example` → `appstore_connect.env` 로 복사 후 값 채우기
   (Issuer ID / Key ID / Team ID / Bundle ID).
2. `AuthKey_<KeyID>.p8` 를 `private_keys/` 에 저장.
3. Mac 에서 빌드한 `.ipa` 를 업로드:
   ```bash
   ./ios/upload.sh game/build/ios/Blockfall.ipa
   ```
   (`build-ios.sh` 도 Team ID / Bundle ID 를 이 파일에서 자동으로 읽으므로,
    로컬 빌드 시 `--team` 을 매번 안 줘도 됩니다.)

## 보안
`appstore_connect.env` 와 `private_keys/*.p8` 는 **`.gitignore` 로 커밋이 차단**됩니다.
절대 저장소/채팅/스크린샷으로 공유하지 마세요. 이 폴더는 `game/`(=Godot `res://`)
**밖**이라 앱 번들(.ipa/.pck)에도 포함되지 않습니다.

## CI(Codemagic)로 빌드한다면 — 이 폴더 불필요
Codemagic 은 같은 API 키를 **웹 UI 볼트**에 보관하고 `codemagic.yaml` 의
`integrations.app_store_connect: blockfall_asc` 로 참조합니다. 태그 하나
(`git tag v1.0.1 && git push origin v1.0.1`)로 TestFlight 까지 자동 업로드되므로
여기 로컬 파일은 필요 없습니다. 전체 절차: [`docs/IOS_RELEASE.md`](../docs/IOS_RELEASE.md).

## 런타임 서비스 ID 는 여기가 아님
Game Center / 인앱결제 product id / AdMob(iOS) 같이 **앱에 컴파일되는 런타임 ID** 는
코드(`game/scripts/Platform/Platforms.cs`)에 있습니다. 이 폴더는 빌드·배포용 **비밀
자격증명** 전용입니다.
