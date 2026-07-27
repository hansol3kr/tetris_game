# STUDIO.md — Blockfall 개발 스튜디오 조직도

이 문서는 Blockfall을 **한 천재 게임 스튜디오**처럼 굴리기 위한 팀 분담·경계·핸드오프 규칙이다.
`CLAUDE.md`(정체성·규칙)의 하위 운영 문서이며, 팀 정의는 `.claude/agents/*.md`에 있다.

## 조직도

```
                          STUDIO HEAD / 크리에이티브 디렉터  (= 메인 오케스트레이터, "Fable")
                          재해석 · 라우팅 · 비전 · 크로스팀 분쟁 조정 · 배포 최종 go/no-go
                                            │
        ┌───────────────────────┬───────────┴───────────┬───────────────────────┐
        ▼                       ▼                       ▼                       ▼
  기능 구현팀              디자인·아트팀             영업팀                 품질보증·릴리스팀
  gameplay-engineering    design-art              publishing-growth       qa-release
  결정론 엔진+넷코드       프레젠테이션+코스메틱      스토어+수익화+시장       검증+CI+릴리스
  [opus]                  [opus]                  [sonnet]                [opus]
        └───────────────────────┴───────────┬───────────┴───────────────────────┘
                                            ▼
                                    플레이테스트팀  playtest  [opus]
                              플레이어의 목소리 — 만들지 않고 느낀다
                          (발견 → studio-head → 해당 팀. 5인 페르소나 패널)
```

> **만드는 팀 4 + 느끼는 팀 1.** 위 4팀은 산출물을 만들고, playtest는 아무것도 만들지 않는다(Edit 툴 없음).
> 이 비대칭이 핵심이다 — 만든 사람이 자기 게임을 평가하면 항상 통과시킨다.

## 핵심 원칙 — 경계는 **폴더가 아니라 결정론/규율 선**을 따른다

`core/` ↔ `game/` 폴더 경계로 팀을 나누면 **Godot 앱-로직 글루**(컨트롤러·Platform 인프라·Net 트랜스포트·수익화 배선)가 어느 팀에도 안 잡히는 순환 구멍이 생긴다(통합 비평가 확인). 그래서 경계는 이렇게 긋는다:

| 레인 | 소유 | 판단 기준 |
|---|---|---|
| **결정론 엔진 + 앱-로직 글루** | gameplay-engineering | "결과가 리플레이·데일리·랭크에 영향? 결정론적 Buttons 마스크를 만들거나 Game을 구동하나?" → YES |
| **프레젠테이션** | design-art | "픽셀·레이아웃·주스·색·타이포? 뷰는 룰을 재구현하지 않고 이벤트만 구독하나?" → YES |
| **스토어·성장** | publishing-growth | "카피·가격·상표·공정성 정책·스토어 심사? 코드가 아니라 문서/전략?" → YES |
| **검증·릴리스** | qa-release | "종료코드로 증명? 게이트·CI·버전·태그·비밀?" → YES |
| **플레이어 체감** | playtest | "**옳게 동작하는가**가 아니라 **좋게 느껴지는가**? 답이 수치가 아니라 감정?" → YES |

> **qa-release vs playtest — 헷갈리지 마라.** qa는 *"스모크 64/64 PASS, 버튼 rect 0×0 아님"* 이라고 말한다.
> playtest는 *"그 버튼 6mm라 엄지로 두 번에 한 번 빗나갔다"* 라고 말한다. 같은 버튼, 다른 질문.
> qa가 그린이어도 playtest는 🔴를 낼 수 있고, 그때 배포를 멈출지는 studio-head가 정한다.

> `ButtonSampler`·`GestureSink`·`GameController`는 `game/scripts/` 안에 있어도 **gameplay** 소유다. 결정론적 입력 글루이기 때문. 반대로 `BoardView`·`Hud`는 같은 폴더라도 **design-art**(뷰).

## 커버리지 매트릭스 (모든 영역이 정확히 한 팀)

| 영역 | 소유 | 비고 |
|---|---|---|
| `core/` 전체(룰·Input·Bot·Progression·Replay·Online·Net·BlockFit·Audio·Localization) | gameplay | Localization/Loc 내용은 타 팀이 핸드오프 생산 |
| `core.tests/` | gameplay(작성) / qa(실행) | 역할 분리 |
| `server/` | gameplay | 넷코드. qa는 `npm test` 게이트만 |
| `game/scripts/Gameplay/` **컨트롤러·입력·런상태** | gameplay | GameController, ButtonSampler, GestureSink, VersusController, BlockFit*, Tutorial*, DailyChallenge, RunResults, KeyBinds … |
| `game/scripts/Gameplay/` **VIEW** | design-art | BoardView, Hud, Juice, BurstEngine, MobilePreview, SafeArea |
| `game/scripts/Platform/` | gameplay | 앱-인프라. PlatformHub에 공정성 광고 캡 **코드** |
| `game/scripts/Net/` 트랜스포트 | gameplay | INetChannel, NetPeer, RelayChannel, NetVersusController |
| `game/scripts/Net/RemoteBoardView` | design-art | 상대 보드 뷰 |
| `game/scripts/UI/` 화면 레이아웃·스타일 | design-art | 모든 화면 + SceneRouter |
| `game/scripts/Theme/` 전체 | design-art | 코드-베이크 에셋 파이프라인 |
| `game/scripts/Audio/AudioManager` | design-art | 플레이백/주스 (신스 수학은 core/Audio → gameplay) |
| `game/shaders/`·`assets/fonts/`·`scenes/Main.tscn`·project.godot[rendering] | design-art | |
| `docs/ARCHITECTURE·NETWORKING` | gameplay | design-art가 view-contract 섹션 기여 |
| `docs/MONETIZATION·STORE_SUBMISSION·appstore-listing·ROADMAP·privacy·support` | publishing | |
| 스토어 콘솔 메타데이터·ASO·가격·SKU 카피 | publishing | |
| `docs/BUILD·DEPLOYMENT·IOS_RELEASE`·실행방법.md | qa | |
| run.sh·build-*.sh·codemagic.yaml·.github/·set-version.py·export_presets.cfg·Blockfall.{sln,csproj}·Dev/AutoPlay.cs·packaging·.gitignore | qa | 버전 단일 소스·스모크 하네스 |
| `ios/` 서명·비밀 | qa(파이프라인) / publishing(비밀 비노출 공동수호) | |
| `CLAUDE.md`·`.claude/`·`README.md` | **studio-head** | 전 팀 disclaim. publishing은 README 마케팅 라인 리뷰 |
| **플레이 체감 리포트**(조작감·첫 5분·페이싱·엄지 도달·다시 켤 이유) | **playtest** | 소유 파일 **0개**. 산출물은 리포트뿐(+스크래치패드 HTML 재현) |

## 분할 파일 심 (한 파일, 라인 단위 소유)

| 파일 | design-art | gameplay | publishing |
|---|---|---|---|
| `Bootstrap.cs` | SceneRouter/Background/ScreenHost/FitScreenHost/ApplyGlowSetting | Platform/Save/StoreCatalog **init 라인** | — |
| `ResultsScreen.cs` / `DescentResultsScreen.cs` | 레이아웃 | 인터스티셜 게이팅 **로직**(`:39`/`:41`) | 광고 캡 **정책**·카피 |
| `StoreScreen.cs` | 레이아웃·라이브 프리뷰 | IAP/entitlement 읽기 로직 | SKU·가격·문안 |
| `StoreCatalog.cs` | — | **코드** 커밋 | Name/Blurb/PriceLabel **카피** |
| `LocData.cs` | 한국어 항목 **생산** | **파일** 소유·append-only enum | 스토어 문자열 원문 |

## 다팀 기능 핸드오프 프로토콜 (예: 새 모드 + HUD + 스토어 SKU + 한국어 카피)

0. **STUDIO HEAD** 요청을 플레이어 경험 언어로 재해석, go/no-go, 첫 수를 gameplay에 라우팅(데이터 우선: GameMode 프리셋으로 되나?).
1. **gameplay** core 설계: `GameMode.ById` 프리셋 + 결정론 영향 평가. 직렬화 변경 시 ReplayData 버전분기 계획. 뷰가 바인딩할 GameEvents+상태 게터+Buttons 로스터 산출.
2. **gameplay** core 로직 + xUnit **같은 커밋**. `dotnet test --filter` 증명. 이벤트/상태 심 + 새 Loc 원문키를 design-art에, 기록 적격 플래그 + SKU 데이터 모델을 publishing에 핸드오프.
3. **publishing** 수익화/공정성 제약(광고 캡, Zen 제외, Second Chance 미기록, SKU, 카피)을 **file:line 스펙**으로 + Loc 영/한 문자열. 배선 스펙을 gameplay에, 샷리스트를 design-art에.
4. **gameplay** publishing 스펙대로 Godot 앱-로직 배선(컨트롤러, PlatformHub 게이팅, ResultsScreen 호출) + 새 모드를 로스터 등록. `_uiHost` 레이아웃 계약을 design-art에.
5. **design-art** 뷰 구축: BoardView/Hud/버스트 FX가 새 GameEvents 구독, BlockTheme/Palette 베이크 + 스토어 라이브 프리뷰, 화면 레이아웃(`UiTheme.ApplyTo`→`SetAnchorsAndOffsetsPreset(FullRect)`), 모든 애니 Motion.Reduced·모든 문자열 Loc.T(), `_ExitTree` 해제.
6. **gameplay + design-art** LocData 항목 공동 작성(영어 원문 = 추가한 팀, 한국어 = 생산 팀) → `core/Localization/LocData.cs`.
6.5. **playtest** 뷰까지 붙은 첫 플레이 가능 상태에서 페르소나 패널 소집 — "이게 재미있나 / 손에 맞나 / 첫 5분에 이탈하나". 🔴는 배포 전에 처리하고, 🟠 이하는 studio-head가 다음 사이클로 넘길지 판정. **기능이 완성된 뒤가 아니라 플레이 가능해진 즉시** 부른다 — 늦게 부르면 이미 못 고친다.
7. **qa** 적대적 게이트(종료코드): --test, --headless(0/0), --smoke(39/39 0×0), `server npm test`, 그리고 직렬화 변경했으니 `ReplayValidator` 재시뮬로 비트 동일/버전분기 확인. 수치 또는 "미수행" 보고.
8. **publishing** 스토어 메타데이터/카피/스크린샷/데이터안전 마감 + 서브미션 준비 신호. 상표 grep(0)·비밀 grep·문자수 게이트.
9. **qa** 모든 게이트 그린 + 마케팅 버전 == 태그일 때만 set-version.py 버전 범프 + `git tag v*.*.*`(되돌릴 수 없는 자동 배포) — publishing 협조 + **studio-head의 명시적 go** 하에.
10. **STUDIO HEAD** 배포 태그 최종 go/no-go + 크로스팀 분쟁(공정성 제약 vs 게임필 요구 등) 조정.

## STUDIO HEAD가 하는, 어느 팀도 안 하는 일

1. **라우팅** — 요청이 어느 레인인지 판정하고, **4팀이 남긴 고아 심**(Platform 인프라·컨트롤러·Net 트랜스포트 등)을 서브팀이 정식 클레임할 때까지 소유. 어떤 서브팀도 자기 경계를 스스로 중재할 수 없다.
2. **재해석** — 모든 요청을 실행 전에 플레이어 경험 언어로 재서술(§1①), 성공 기준을 세운다.
3. **비전·트레이드오프** — 제품/미학 북극성(NEON GLOSS 프리미엄, 세로 고정, 오리지널 브랜드 세이프하버) 유지, 한 팀의 불가침 규칙이 다른 팀 목표와 충돌할 때 조정(publishing 광고 수익 vs gameplay 기록 무결성, design-art 주스 vs qa 결정론/Reduced).
4. **되돌릴 수 없는 최종 결정** — `git tag v*.*.*` 자동 배포 인가는 **오직 studio-head**. qa가 게이트를 엔지니어링하되 배포 go/no-go는 studio-head.
5. **헌장 자체의 수호** — `CLAUDE.md`·`.claude/`는 studio-head만 개정. 조직 설계·불가침 규칙·팀 경계 변경 권한.

## 팀 부르는 법

- `"디자인팀아 결과 화면 콤보 팝업 더 화려하게"` → **design-art** 라우팅
- `"기능팀, 새 Marathon 변형 모드 추가"` → **gameplay-engineering**
- `"영업팀, 1.2 App Store 설명문이랑 키워드 써줘"` → **publishing-growth**
- `"품질팀, 릴리스 v1.1.2 태그 준비"` → **qa-release**
- `"테스트팀, 이번 드래그 조작 써보고 솔직하게 말해줘"` → **playtest** (페르소나 미지정 시 변경에 가장 아픈 1~2인을 스스로 선택)
- `"테스트팀 전원 소집"` → studio-head가 5인 페르소나(stacker/commuter/newbie/a11y/collector)를 **병렬 패널**로 소집, 리포트를 합쳐 중복 제거 후 담당팀에 라우팅
- `"기능팀·영업팀 같이 신규 코스메틱 모드 하나 뽑아봐"` → studio-head가 위 핸드오프 프로토콜로 병렬 오케스트레이션
- 명시적 팀 지정이 없으면 studio-head가 커버리지 매트릭스로 라우팅.

## 모델 배정 근거

- **opus**: gameplay(결정론·직렬화·넷코드 — 조용한 desync 비용 극단), design-art(0×0/안전영역/수명주기 미묘 버그 + 시각 판단), qa(되돌릴 수 없는 배포·결정론 감사·비밀 스캔).
- **opus**: playtest — 페르소나 렌즈 유지 + **거짓 관찰 금지**가 이 팀의 전부다. 헤드리스에서 "본 척"하는 리포트는 스튜디오 전체를 잘못된 수정으로 끌고 간다. 판단 품질이 곧 신뢰.
- **sonnet**: publishing(카피·문서·grep 감사 중심) — **단, 상표/법률 인접 감사나 3개 스토어 정책 교차검증은 opus로 승격.**
- 모든 배정은 호출 시 상황에 따라 override 가능(기계적 패스는 하향, 어려운 판단은 상향).
