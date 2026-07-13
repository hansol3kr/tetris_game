# ios/private_keys/

App Store Connect API **개인키(.p8)** 를 여기에 두세요:

```
ios/private_keys/AuthKey_<KeyID>.p8
```

- App Store Connect → **Users and Access → Integrations → App Store Connect API**
  에서 키 생성 시 **딱 한 번** 다운로드됩니다. 잃어버리면 그 키를 폐기(Revoke)하고
  새로 만들어야 합니다 — 안전한 곳에 백업하세요.
- 이 `.p8` 는 **비밀**입니다. `.gitignore` 가 `ios/private_keys/*.p8` 커밋을 막습니다 —
  절대 커밋/공유 금지. (이 README 만 저장소에 올라갑니다.)
- `ios/upload.sh` 와 `xcrun altool` 이 이 키를 자동으로 찾아 씁니다
  (`ios/appstore_connect.env` 의 `ASC_KEY_ID` 와 파일명 `<KeyID>` 가 일치해야 함).
