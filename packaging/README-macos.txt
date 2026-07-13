Blockfall — macOS 독립 실행판 (Universal: Intel + Apple Silicon)
================================================================

■ 실행 방법
  1) Blockfall.zip 압축을 풉니다 → Blockfall.app 이 나옵니다.
  2) Blockfall.app 을 "오른쪽 클릭 → 열기" 로 실행합니다.
     (그냥 더블클릭하면 "확인되지 않은 개발자" 경고로 막힙니다.)

  * 여전히 막히면 터미널에서 격리 속성을 제거하세요:
        xattr -dr com.apple.quarantine /경로/Blockfall.app

  이 빌드는 코드 서명/공증이 되어 있지 않습니다(맥 없이 만들었기 때문).
  정식 배포하려면 Apple Developer 계정으로 서명·공증이 필요합니다.

■ 필요 환경
  - macOS (Intel 또는 Apple Silicon 모두 지원 — universal 바이너리)
  - .NET / Godot 설치 불필요 (전부 포함)

■ 조작 (키보드)
  ← / →  이동   ↓ 소프트드롭   Space 하드드롭
  ↑ / Z  회전   A 180도   C 홀드   Esc 일시정지
  (게임패드 지원)
