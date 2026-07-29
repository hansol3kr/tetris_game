using System.Collections.Generic;

namespace Blockfall.Core.Localization;

/// <summary>
/// Korean UI translations, keyed by the exact English source string used at the
/// call site. Any wrapped string with no entry here falls back to English, so the
/// table can grow incrementally. Pure-punctuation/format templates (e.g. "{0} {1}",
/// "CPU · {0}") are intentionally omitted — translating them would just reproduce
/// the source. Grouped by screen; shared keys live at the top.
/// </summary>
internal static class LocData
{
    internal static readonly Dictionary<string, string> Korean = new()
    {
        // ---- Shared / common ----------------------------------------------
        ["BACK"] = "뒤로",
        ["MENU"] = "메뉴",
        ["SCORE"] = "점수",
        ["LINES"] = "라인",
        ["LEVEL"] = "레벨",
        ["TIME"] = "시간",
        ["SEED"] = "시드",
        ["QUADS"] = "쿼드",
        ["T-SPINS"] = "T-스핀",
        ["FINESSE"] = "피네스",
        ["PROFILE"] = "프로필",
        ["STORE"] = "상점",
        ["REPLAYS"] = "리플레이",
        ["SETTINGS"] = "설정",
        ["PAUSED"] = "일시정지",
        ["RESUME"] = "계속하기",
        ["YOU WIN!"] = "승리!",
        ["DEFEATED"] = "패배",
        ["REMATCH"] = "재대결",
        ["OPPONENT LEFT"] = "상대가 나갔습니다",

        // ---- Main menu ----------------------------------------------------
        ["PLAY"] = "플레이",
        ["DAILY CHALLENGE"] = "일일 챌린지",
        ["ONE SEED, ONE SHOT · NEW SEED IN {0}H {1}M"] = "하나의 시드, 한 번의 도전 · 새 시드까지 {0}시간 {1}분",
        ["VERSUS CPU"] = "CPU 대전",
        ["GARBAGE BATTLE · FIVE DIFFICULTIES"] = "방해 블록 배틀 · 5단계 난이도",
        ["CUSTOM RUN"] = "커스텀 런",
        ["MODIFIERS STACK ON ANY RUN · MODIFIED RUNS SET NO RECORDS"] = "모디파이어는 모든 런에 중첩 · 변경된 런은 기록되지 않음",
        ["SEED CODE OR ANY WORD"] = "시드 코드 또는 아무 단어",
        ["PLAY SEED"] = "시드 플레이",
        ["HOW TO PLAY"] = "게임 방법",
        ["FIRST RUN"] = "첫 도전",
        ["ORIGINAL BRAND · ALL ART AND RULES ARE OUR OWN"] = "독자 브랜드 · 아트와 규칙 전부 자체 제작",

        // Mode titles (display only — the GameModeId enum is the persisted key).
        ["MARATHON"] = "마라톤",
        ["SPRINT 40"] = "스프린트 40",
        ["ULTRA 2:00"] = "울트라 2:00",
        ["ZEN"] = "젠",
        ["DIG RACE"] = "디그 레이스",
        ["SURVIVAL"] = "서바이벌",
        ["MASTER 20G"] = "마스터 20G",
        // Mode blurbs.
        ["CLIMB TO LEVEL 15"] = "레벨 15까지 오르기",
        ["40 LINES, FASTEST TIME"] = "40라인, 최단 시간",
        ["MAX SCORE IN 2 MINUTES"] = "2분간 최고 점수",
        ["NO PRESSURE, NO END"] = "부담 없이, 끝없이",
        ["DIG THROUGH THE GARBAGE"] = "방해 블록 파헤치기",
        ["THE FLOOR KEEPS RISING"] = "바닥이 계속 차오른다",
        ["INSTANT GRAVITY"] = "즉시 낙하 중력",

        // Run modifiers (from core ModifierSet.Label).
        ["NO HOLD"] = "홀드 없음",
        ["NO GHOST"] = "고스트 없음",
        ["LOW-G"] = "저중력",
        ["BLITZ"] = "블리츠",
        ["HARD LOCK"] = "하드 락",
        ["ALL-SPIN"] = "올 스핀",
        ["INVISIBLE"] = "인비저블",

        // ---- Settings -----------------------------------------------------
        ["LANGUAGE"] = "언어",
        ["SKIN"] = "스킨",
        ["AUDIO"] = "오디오",
        ["VISUAL"] = "화면",
        ["HANDLING"] = "조작",
        ["SFX VOLUME"] = "효과음 볼륨",
        ["MUSIC VOLUME"] = "음악 볼륨",
        ["MUTE"] = "음소거",
        ["PLACE SOUND"] = "배치 사운드",
        // Placement sound packs. Plain nouns only — no switch brands or model names.
        ["NEON"] = "네온",
        ["TAP"] = "탭",
        ["CLICK"] = "클릭",
        ["TYPEBAR"] = "타자기",
        ["WOOD"] = "우드",
        ["GHOST PIECE"] = "고스트 블록",
        ["COLORBLIND PALETTE"] = "색약 팔레트",
        ["FINESSE METER"] = "피네스 미터",
        ["NEON GLOW (BLOOM)"] = "네온 글로우 (블룸)",
        ["REDUCED MOTION"] = "모션 줄이기",
        ["JUICE / SHAKE"] = "이펙트 / 흔들림",
        ["CLEAR EFFECT"] = "클리어 이펙트",
        // Clear-effect style values: the picker shows the VALUE, which went untranslated
        // until CycleRow started running names through Loc.T. "BURST" is deliberately
        // absent here: it is already keyed further down (the Descent stage name) and
        // this is one flat dictionary — a second entry would silently win by position,
        // which is how a translation quietly changes on an unrelated screen.
        ["SPARKS"] = "스파크",
        ["CONFETTI"] = "컨페티",
        ["MINIMAL"] = "미니멀",
        ["SHATTER"] = "파편",
        ["FULLSCREEN"] = "전체 화면",
        ["DAS (DELAY)"] = "DAS (지연)",
        ["ARR (REPEAT)"] = "ARR (반복)",
        ["DRAG CONTROLS (TOUCH)"] = "드래그 조작 (터치)",
        ["UNAVAILABLE ON THIS DEVICE"] = "이 기기에서 사용 불가",
        ["ON"] = "켜짐",
        ["OFF"] = "꺼짐",
        // Controls (keyboard remap)
        ["CONTROLS"] = "키 설정",
        ["CLICK A KEY, THEN PRESS THE NEW ONE"] = "키를 누른 뒤 새 키를 입력하세요",
        ["PRESS A KEY…"] = "키를 누르세요…",
        ["RESET TO DEFAULTS"] = "기본값으로 초기화",
        ["MOVE LEFT"] = "왼쪽 이동",
        ["MOVE RIGHT"] = "오른쪽 이동",
        ["SOFT DROP"] = "소프트 드롭",
        ["HARD DROP"] = "하드 드롭",
        ["ROTATE CW"] = "시계 방향 회전",
        ["ROTATE CCW"] = "반시계 방향 회전",
        ["ROTATE 180"] = "180도 회전",
        ["PAUSE"] = "일시정지",
        // Accessibility
        ["ACCESSIBILITY"] = "접근성",
        ["TEXT SIZE"] = "글자 크기",
        ["BLOCK COLORS · APPLIES INSTANTLY"] = "블록 색상 · 즉시 적용",
        ["MORE SKINS IN THE STORE"] = "상점에서 더 많은 스킨",
        ["ACTIVE"] = "사용 중",

        // ---- In-game HUD --------------------------------------------------
        ["GOAL"] = "목표",
        ["HOLD"] = "홀드",
        ["NEXT"] = "다음",
        ["✓ CLEAN"] = "✓ 완벽",
        ["CLEAN ×{0}"] = "완벽 ×{0}",
        ["{0} left"] = "{0} 남음",
        ["{0} rows"] = "{0} 줄",
        ["STREAK ×{0}"] = "연속 ×{0}",
        // Block Fit fuse budget: remaining / cap. Both numbers are shown because the cap is the
        // rule ("clearing a line earns one back, up to this") and the colour is only redundant.
        ["FUSE {0}/{1}"] = "조합 {0}/{1}",
        ["BEST {0}"] = "최고 {0}",
        ["BEST STREAK ×{0}"] = "최고 연속 ×{0}",
        ["NEW BEST"] = "신기록",

        // ---- Touch coaching (gesture hint + Block Fit first run) ----------
        ["DRAG TO LINE UP   ·   LIFT TO DROP   ·   TAP TO ROTATE"] = "끌어서 맞추고   ·   손을 떼면 내려가고   ·   탭하면 회전",
        ["BLOCK FIT"] = "블록 핏",
        // Same card, shown a second time to players who only ever saw its 3-line version.
        ["NEW  ·  FUSE & HOLD"] = "새 기능  ·  조합과 홀드",
        ["1  ·  DRAG a piece from the tray onto the grid."] = "1  ·  아래 트레이의 조각을 끌어다 판에 놓습니다.",
        ["2  ·  FILL a whole row or column and it clears."] = "2  ·  가로 또는 세로 한 줄을 채우면 사라집니다.",
        ["3  ·  It ends when none of the three pieces fit."] = "3  ·  세 조각 모두 놓을 자리가 없으면 끝납니다.",
        ["3  ·  DROP a piece on ANOTHER TRAY PIECE to fuse them."] = "3  ·  다른 트레이 조각 위에 놓으면 두 조각이 합쳐집니다.",
        ["3  ·  DROP a piece on ANOTHER TRAY PIECE to fuse them — fuses are limited, and clearing a line earns one back."]
            = "3  ·  다른 트레이 조각 위에 놓으면 두 조각이 합쳐집니다 — 조합 횟수는 제한되어 있고, 줄을 지우면 1회 회복됩니다.",
        ["4  ·  DROP a piece on the HOLD slot to save it for later."] = "4  ·  홀드 칸에 놓으면 나중을 위해 보관합니다.",
        ["5  ·  It ends when none of the three pieces fit."] = "5  ·  세 조각 모두 놓을 자리가 없으면 끝납니다.",
        ["GOT IT"] = "알겠어요",

        // ---- In-game overlays (pause / revive / ghost) --------------------
        ["GHOST"] = "고스트",
        ["GHOST  {0}{1}"] = "고스트  {0}{1}",
        ["SECOND CHANCE?"] = "다시 한 번?",
        ["WIPE THE BOARD, KEEP YOUR RUN.\nREVIVED RUNS SET NO RECORDS."] = "보드를 비우고 게임을 이어가세요.\n부활한 게임은 기록으로 남지 않습니다.",
        ["USE SECOND CHANCE ({0} LEFT)"] = "다시 한 번 사용 ({0}개 남음)",
        ["WATCH AD TO REVIVE"] = "광고 보고 부활하기",
        ["GIVE UP"] = "포기하기",
        ["QUIT TO MENU"] = "메뉴로 나가기",

        // ---- Versus (CPU + online) ---------------------------------------
        ["YOU"] = "나",
        ["VS CPU · {0}"] = "CPU 대전 · {0}",
        ["YOU {0}"] = "나 {0}",
        ["CPU {0}"] = "CPU {0}",
        ["INCOMING +{0}"] = "공격 +{0}",
        ["SENT +{0}"] = "보냄 +{0}",
        ["YOU {0}  ·  CPU {1}"] = "나 {0}  ·  CPU {1}",
        ["YOU   {0}L"] = "나   {0}L",
        ["YOU   {0}L · SENT {1}"] = "나   {0}줄 · 보냄 {1}",
        ["{0}   {1}L · SENT {2}"] = "{0}   {1}줄 · 보냄 {2}",
        ["LEAVE THE MATCH?"] = "경기를 나갈까요?",
        ["KEEP PLAYING"] = "계속하기",
        ["FORFEIT"] = "기권",
        ["WAITING FOR OPPONENT…"] = "상대를 기다리는 중…",
        ["WAITING FOR HOST…"] = "호스트를 기다리는 중…",
        ["{0} WANTS A REMATCH"] = "{0}님이 재대결을 원합니다",
        ["STARTING…"] = "시작하는 중…",

        // ---- Results screen ----------------------------------------------
        ["CLEAR!"] = "클리어!",
        ["GAME OVER"] = "게임 오버",
        ["ACHIEVEMENT: {0}"] = "업적: {0}",
        ["MAX COMBO"] = "최대 콤보",
        ["PIECES/S"] = "초당 블록",
        ["SENT"] = "보낸 방해",
        ["FAULTS"] = "실수",
        ["BEST STREAK"] = "최고 연속",
        ["MODS"] = "모디파이어",
        ["PLAY AGAIN"] = "다시 하기",
        ["WATCH REPLAY"] = "리플레이 보기",
        ["SAVE REPLAY"] = "리플레이 저장",
        ["SAVED"] = "저장됨",
        ["SAVE FAILED"] = "저장 실패",
        ["SHARE CODE"] = "코드 공유",
        ["COPIED"] = "복사됨",
        ["REPLAY SEED"] = "시드 재도전",

        // ---- Profile screen ----------------------------------------------
        ["GAMES PLAYED"] = "플레이 횟수",
        ["TOTAL SCORE"] = "총 점수",
        ["LINES CLEARED"] = "제거한 라인",
        ["PIECES PLACED"] = "배치한 블록",
        ["TIME PLAYED"] = "플레이 시간",
        ["PERFECT CLEARS"] = "퍼펙트 클리어",
        ["BEST COMBO"] = "최고 콤보",
        ["BEST BACK-TO-BACK"] = "최고 백투백",
        ["BEST PIECES/SEC"] = "최고 초당 블록",
        ["{0} / {1} UNLOCKED"] = "{0} / {1} 달성",
        ["No entries yet — play this mode to rank."] = "기록이 없습니다 — 이 모드를 플레이해 순위에 등록하세요.",
        ["{0}h {1}m"] = "{0}시간 {1}분",
        ["{0}m"] = "{0}분",

        // ---- Store screen -------------------------------------------------
        ["THEMES"] = "테마",
        ["BURST FX"] = "클리어 연출",
        ["BLOCK FIT · DRAG & FIT, NO GRAVITY"] = "블록 핏 · 끼워 맞추기, 중력 없음",
        ["BOOSTERS"] = "부스터",
        ["PREMIUM"] = "프리미엄",
        ["RESTORE PURCHASES"] = "구매 복원",
        ["EQUIPPED"] = "장착됨",
        ["EQUIP"] = "장착",
        ["NEW"] = "신규",
        ["COLOR-SAFE PALETTE IS ON: THE BOARD KEEPS IT. PREVIEWS BELOW SHOW EACH SKIN'S OWN COLORS."]
            = "색약 팔레트 사용 중: 보드는 색약 팔레트를 유지합니다. 아래 미리보기는 각 스킨 고유의 색을 보여줍니다.",
        ["{0}   ·   OWNED: {1}"] = "{0}   ·   보유: {1}",
        ["INCLUDES THE {0} BURST"] = "{0} 클리어 연출 포함",
        ["SOUND"] = "사운드",
        ["HEAR"] = "들어보기",
        ["EVERY PACK IS FREE AND ALREADY YOURS. TAP A WAVEFORM TO HEAR IT, EQUIP TO KEEP IT — IT CHANGES ONLY HOW A PLACED PIECE SOUNDS."]
            = "모든 팩은 무료이며 이미 보유 중입니다. 파형을 눌러 들어보고, 장착을 눌러 적용하세요 — 블록을 놓을 때 나는 소리만 바뀝니다.",

        // ---- Catalog copy: EVERY item's name and blurb ----------------------
        // StoreScreen runs item.Name and item.Blurb through Loc.T, so a partial table does not
        // degrade gracefully here the way it does elsewhere: an untranslated row does not look
        // "not yet localized", it looks like a bug, because the row above it is Korean. The
        // shelf is the one screen where the fallback rule (missing key ⇒ English) produces a
        // WORSE result than no translation at all — one screen, two languages, alternating by
        // row. So this block is exhaustive by contract: adding a catalog item means adding its
        // two strings here in the same edit.
        // Product names are transliterated rather than translated (네온 플럭스, not 네온 흐름):
        // they are names, they appear in store listings and screenshots, and the animal sets
        // above already set that precedent.

        // Free themes.
        ["NEON FLUX"] = "네온 플럭스",
        ["THE ORIGINAL. ALWAYS YOURS."] = "오리지널. 언제나 당신의 것.",
        ["AURORA"] = "오로라",
        ["COOL GREEN NIGHT, TEAL HORIZON."] = "서늘한 초록빛 밤, 청록 지평선.",
        ["TIDE"] = "타이드",
        ["DEEP OCEAN BLUES, COOL NEON."] = "깊은 바다의 푸른빛, 차가운 네온.",
        ["CRIMSON"] = "크림슨",
        ["DARK RED DUSK, WARM GLOW."] = "짙은 붉은 노을, 따뜻한 빛.",
        ["SYNTHWAVE"] = "신스웨이브",
        ["PURPLE DUSK, MAGENTA & CYAN."] = "보랏빛 황혼, 마젠타와 시안.",
        ["MINT"] = "민트",
        ["SOFT PASTEL, COOL MINT NIGHT."] = "부드러운 파스텔, 시원한 민트빛 밤.",
        ["EMBER"] = "엠버",
        ["WARM AMBER COALS, GOLD GLOW."] = "따스한 호박빛 잉걸불, 금빛 광채.",

        // Glyph skins.
        ["SMILEY POP"] = "스마일 팝",
        ["CANDY BRIGHTS WITH A HAPPY FACE ON EVERY BLOCK."] = "사탕처럼 선명한 색, 모든 블록에 웃는 얼굴.",
        ["STARLIGHT"] = "스타라이트",
        ["COSMIC NEON WITH A STAR ON EVERY BLOCK."] = "우주빛 네온, 모든 블록에 별 하나.",
        ["LOVECORE"] = "러브코어",
        ["ROSY POP WITH A HEART ON EVERY BLOCK."] = "장밋빛 팝, 모든 블록에 하트 하나.",
        ["BLOOM"] = "블룸",
        ["SPRING BRIGHTS WITH A FLOWER ON EVERY BLOCK."] = "봄빛 밝은 색, 모든 블록에 꽃 한 송이.",
        ["VOLTAGE"] = "볼티지",
        ["ELECTRIC NEON WITH A BOLT ON EVERY BLOCK."] = "전기 네온, 모든 블록에 번개 하나.",
        ["MEOW"] = "야옹",
        ["CREAMY PASTELS WITH A KAWAII CAT ON EVERY BLOCK."] = "크리미한 파스텔, 모든 블록에 귀여운 고양이.",
        ["ROYALE"] = "로열",
        ["REGAL PURPLE AND GOLD WITH A JEWELLED CROWN ON EVERY BLOCK."] = "고귀한 보라와 금빛, 모든 블록에 보석 왕관.",
        ["MIDNIGHT"] = "미드나이트",
        ["DEEP VIOLET NIGHT WITH A CRESCENT MOON ON EVERY BLOCK."] = "짙은 보랏빛 밤, 모든 블록에 초승달.",
        ["LUCKY"] = "럭키",
        ["FRESH GREENS WITH A LUCKY CLOVER ON EVERY BLOCK."] = "싱그러운 초록, 모든 블록에 행운의 클로버.",
        ["SPOOKY"] = "스푸키",
        ["HAUNTED GREEN AND PURPLE WITH A BONE-WHITE SKULL ON EVERY BLOCK."] = "으스스한 초록과 보라, 모든 블록에 뼛빛 해골.",
        ["PRIDE"] = "프라이드",
        ["JOYFUL BRIGHTS WITH A RAINBOW ON EVERY BLOCK."] = "즐거운 밝은 색, 모든 블록에 무지개.",

        // Material skins.
        ["SUNSET DRIVE"] = "선셋 드라이브",
        ["WARM DUSK PINKS OVER A PURPLE HORIZON."] = "보랏빛 지평선 위로 번지는 따뜻한 노을빛 분홍.",
        ["DEEP EMERALD"] = "딥 에메랄드",
        ["JEWEL TONES ON A DARK GREEN SEA."] = "짙은 초록 바다 위의 보석빛.",
        ["MONO ARCADE"] = "모노 아케이드",
        ["PURE GRAYSCALE. SHAPES ONLY. NO MERCY."] = "순수한 무채색. 오직 모양뿐. 봐주는 것 없이.",
        ["BUBBLEGUM"] = "버블검",
        ["WET PASTEL GEL YOU COULD ALMOST CHEW, WITH A HEART ON EVERY BLOCK."] = "씹힐 듯 촉촉한 파스텔 젤리, 모든 블록에 하트 하나.",
        ["GLACIER"] = "글레이셔",
        ["MATTE FROSTED PEBBLES — THE QUIET, CALM PREMIUM."] = "무광 서리 조약돌 — 조용하고 차분한 프리미엄.",
        ["GALAXY"] = "갤럭시",
        ["DEEP-SPACE JEWELS WITH STARS THAT TWINKLE ON EVERY BLOCK."] = "심우주의 보석, 모든 블록에서 반짝이는 별.",
        ["CIRCUIT"] = "서킷",
        ["BRUSHED-METAL CELLS WITH A LIVE TEAL EDGE AND A BOLT ON EVERY BLOCK."] = "헤어라인 금속 셀에 살아 있는 청록 테두리, 모든 블록에 번개 하나.",
        ["PRISM"] = "프리즘",
        ["NEON GLASS THAT BLEEDS IRIDESCENCE, WITH A GEM ON EVERY BLOCK."] = "무지갯빛이 번지는 네온 유리, 모든 블록에 보석 하나.",
        ["OBSIDIAN"] = "옵시디언",
        ["FACETED BLACK GLASS THAT BLEEDS RAINBOW AT EVERY EDGE."] = "모서리마다 무지개가 번지는 각진 검은 유리.",

        // ColorPlan skins.
        ["OAK"] = "오크",
        ["THREE TIMBER TONES. MATTE GRAIN, NO GLOSS — IT BREAKS INTO SPLINTERS."]
            = "세 가지 목재 톤. 무광 나뭇결, 광택 없음 — 부서지면 나뭇조각이 됩니다.",
        ["SUMI"] = "스미",
        ["ONE INK, SEVEN SHADES. FROSTED STONE — THE QUIETEST BOARD IN THE GAME."]
            = "하나의 먹, 일곱 단계 농담. 서리 낀 돌 — 게임에서 가장 고요한 보드.",
        ["VAPOR TUBE"] = "베이퍼 튜브",
        ["TWO GASES IN HOLLOW GLASS. IT SHATTERS INTO GLOWING ARCS."]
            = "속이 빈 유리관 속 두 기체. 부서지면 빛나는 호가 흩어집니다.",
        ["DICHROIC"] = "다이크로익",
        ["THE BOARD ITSELF IS THE GRADIENT — CYAN AT ONE CORNER, VIOLET AT THE OTHER."]
            = "보드 전체가 하나의 그러데이션 — 한쪽 모서리는 시안, 반대쪽은 보라.",

        // Burst-FX artifacts (AURORA reuses the theme entry above — one flat table, one key).
        ["SPARKLE"] = "스파클",
        ["THE ORIGINAL GOLDEN SPARK BURST."] = "오리지널 황금빛 스파크 연출.",
        ["FIREWORKS"] = "불꽃놀이",
        ["MULTICOLOUR BURSTS AND BOOMING RINGS."] = "여러 색의 폭발과 울려 퍼지는 고리.",
        ["A RAIN OF COLOURFUL PAPER BITS."] = "알록달록한 종잇조각의 비.",
        ["SUPERNOVA"] = "슈퍼노바",
        ["A BLINDING FLASH AND A HUGE SHOCKWAVE."] = "눈부신 섬광과 거대한 충격파.",
        ["PRISM SHARDS"] = "프리즘 파편",
        ["CHUNKY NEON SHARDS BLASTED OUTWARD."] = "굵직한 네온 파편이 사방으로 터집니다.",
        ["RAINBOW WAVE"] = "레인보우 웨이브",
        ["A FULL-SPECTRUM SWEEP ALONG THE LINE."] = "라인을 따라 흐르는 전 스펙트럼 물결.",
        ["SOFT CURTAINS OF LIGHT RISE INSTEAD OF A BURST."] = "폭발 대신 부드러운 빛의 장막이 피어오릅니다.",
        ["LIGHTNING"] = "라이트닝",
        ["ELECTRIC ARCS SNAP ACROSS EACH CLEARED LINE."] = "지워진 줄마다 전기 아크가 튑니다.",
        ["BUBBLE POP"] = "버블 팝",
        ["IRIDESCENT SOAP BUBBLES DRIFT UP AND POP."] = "무지갯빛 비눗방울이 떠올라 터집니다.",
        ["PRISM BLOOM"] = "프리즘 블룸",
        ["CLEARS DISSOLVE INTO A RISING CLOUD OF RAINBOW LIGHT."] = "지워진 줄이 피어오르는 무지갯빛 구름으로 흩어집니다.",
        ["STARFALL"] = "스타폴",
        ["A SHOWER OF METEORS STREAKS ACROSS THE CLEAR."] = "유성우가 지워진 자리를 가로지릅니다.",

        // Sound packs (the NAMES are already keyed under Settings › PLACE SOUND).
        ["THE ORIGINAL LOCK TONE. ALWAYS YOURS."] = "오리지널 배치음. 언제나 당신의 것.",
        ["SOFT RUBBER DOME. QUIET DESK, LATE NIGHT."] = "부드러운 러버돔. 조용한 책상, 늦은 밤.",
        ["CLICKY SWITCH. TWO SNAPS PER PLACE."] = "클릭 스위치. 한 번 놓을 때 두 번의 딸깍.",
        ["STEEL HAMMER ON A PLATEN."] = "압반을 때리는 강철 해머.",
        ["MALLET ON A WOODEN BAR. WARM AND DRY."] = "나무 건반을 두드리는 말렛. 따뜻하고 건조하게.",

        // Booster + entitlement.
        ["SECOND CHANCE ×3"] = "다시 한 번 ×3",
        ["TOP OUT, WIPE THE BOARD, KEEP THE RUN. SOLO MODES ONLY — REVIVED RUNS SET NO RECORDS."]
            = "가득 차도 보드를 비우고 게임을 이어갑니다. 솔로 모드 전용 — 부활한 게임은 기록으로 남지 않습니다.",
        ["REMOVE ADS"] = "광고 제거",
        ["NO MORE INTERSTITIALS. FOREVER."] = "전면 광고 없음. 영원히.",

        // ---- Animal skin sets (name + blurb + signature burst) -------------
        ["PELT"] = "펠트",
        ["THREE COAT TONES. MATTE STRAND GRAIN — IT SHEDS, IT DOESN'T SHATTER."]
            = "세 가지 털색. 무광 결 — 부서지지 않고 흩날립니다.",
        // Renamed from SERPENTINE (the set is a fish everywhere except its old name); the
        // save key is still theme_serpentine, only the display string moved.
        ["DORSAL"] = "등지느러미",
        ["ONE BODY ACROSS THE WHOLE BOARD. WET OVERLAPPING SCALES."]
            = "보드 전체가 하나의 몸통. 젖은 겹비늘.",
        ["CARAPACE"] = "카라페이스",
        ["ONE LACQUERED HUE, SEVEN RUNGS. THE HARDEST BOARD IN THE GAME."]
            = "옻칠한 단색, 일곱 단계. 게임에서 가장 단단한 보드.",
        ["FLUFF"] = "플러프",
        ["SOFT TUFTS DRIFT UP AND SETTLE. NO FLASH, NO GLARE."]
            = "부드러운 털뭉치가 떠올랐다 내려앉습니다. 섬광도 눈부심도 없이.",
        ["SPLASH"] = "스플래시",
        ["A COLUMN OF WATER, TWO RIPPLES, THEN FALLING DROPS."]
            = "물기둥, 두 겹의 파문, 그리고 떨어지는 물방울.",
        ["SWARM"] = "스웜",
        ["THE SHELL CRACKS ON ONE RING AND SCATTERS."]
            = "껍질이 한 겹의 고리로 갈라지며 흩어집니다.",

        // ---- Replays screen ----------------------------------------------
        ["paste a replay share code"] = "리플레이 공유 코드 붙여넣기",
        ["IMPORT"] = "가져오기",
        ["No saved replays yet — save one from the results screen."] = "저장된 리플레이가 없습니다 — 결과 화면에서 저장하세요.",
        ["{0} lines · {1}"] = "{0}줄 · {1}",
        ["WATCH"] = "보기",

        // ---- Versus select screen ----------------------------------------
        ["ONLINE MATCH"] = "온라인 대전",
        ["DUEL A FRIEND — HOST OR JOIN BY IP"] = "친구와 대결 — 방 만들기 또는 IP로 참가",
        ["OR CHOOSE A CPU OPPONENT"] = "또는 CPU 상대를 선택하세요",
        ["EASY"] = "쉬움",
        ["NORMAL"] = "보통",
        ["HARD"] = "어려움",
        ["MASTER"] = "마스터",
        ["GRANDMASTER"] = "그랜드마스터",

        // ---- Ranked ladder (tiers + readouts) -----------------------------
        // MASTER / GRANDMASTER reuse the CPU-difficulty translations above.
        ["BRONZE"] = "브론즈",
        ["SILVER"] = "실버",
        ["GOLD"] = "골드",
        ["PLATINUM"] = "플래티넘",
        ["DIAMOND"] = "다이아몬드",
        ["YOUR RANK: UNRANKED"] = "내 랭크: 미배치",
        ["YOUR RANK: {0} · {1}  ({2}W {3}L)"] = "내 랭크: {0} · {1}  ({2}승 {3}패)",
        ["RANK {0}  ({1}{2}) · {3}"] = "랭크 {0}  ({1}{2}) · {3}",

        // ---- Online lobby -------------------------------------------------
        ["SERVERLESS 1V1 — SAME WIFI, OR THE HOST FORWARDS PORT 7777"] = "서버리스 1대1 — 같은 와이파이, 또는 호스트가 7777 포트 개방",
        ["HOST A MATCH"] = "방 만들기",
        ["YOUR ADDRESS: {0}"] = "내 주소: {0}",
        ["YOUR ADDRESS: (NO LAN ADDRESS FOUND)"] = "내 주소: (LAN 주소를 찾을 수 없음)",
        ["OPEN ROOM"] = "방 열기",
        ["JOIN A MATCH"] = "방 참가",
        ["HOST IP  (E.G. 192.168.0.10)"] = "호스트 IP  (예: 192.168.0.10)",
        ["JOIN"] = "참가",
        ["QUICK MATCH  —  FIND ANY OPPONENT ONLINE"] = "빠른 대전  —  온라인 상대 자동 찾기",
        ["matchmaker url (ws://host:8080)"] = "매치메이커 URL (ws://host:8080)",
        ["FIND MATCH"] = "상대 찾기",
        // Online lobby status messages.
        ["COULD NOT OPEN PORT {0} ({1})"] = "{0} 포트를 열 수 없습니다 ({1})",
        ["ROOM OPEN ON PORT {0} — WAITING FOR AN OPPONENT…"] = "{0} 포트에서 방이 열렸습니다 — 상대를 기다리는 중…",
        ["ENTER THE HOST'S IP FIRST"] = "먼저 호스트의 IP를 입력하세요",
        ["COULD NOT CONNECT ({0})"] = "연결할 수 없습니다 ({0})",
        ["CONNECTING TO {0}:{1}…"] = "{0}:{1}에 연결하는 중…",
        ["ENTER THE MATCHMAKER URL FIRST"] = "먼저 매치메이커 주소를 입력하세요",
        ["MATCH FOUND — VS {0}"] = "상대를 찾았습니다 — VS {0}",
        ["SEARCHING FOR AN OPPONENT…"] = "상대를 찾는 중…",
        ["CONNECTED — SHAKING HANDS…"] = "연결됨 — 핸드셰이크 중…",
        ["CONNECTION LOST"] = "연결이 끊겼습니다",
        ["VERSION MISMATCH — BOTH PLAYERS NEED THE SAME BUILD"] = "버전 불일치 — 두 플레이어 모두 같은 빌드여야 합니다",

        // ---- Tutorial -----------------------------------------------------
        ["MOVE  —  slide the piece left and right"] = "이동  —  블록을 좌우로 움직이기",
        ["◀  ▶  arrow keys"] = "◀  ▶  방향키",
        ["◀  ▶  buttons"] = "◀  ▶  버튼",
        ["ROTATE  —  turn the piece"] = "회전  —  블록을 돌리기",
        ["▲ (or Z)"] = "▲ (또는 Z)",
        ["SOFT DROP  —  hold to fall faster"] = "소프트 드롭  —  길게 눌러 빠르게 내리기",
        ["▼ down arrow"] = "▼ 아래 방향키",
        ["▼ button"] = "▼ 버튼",
        ["HARD DROP  —  slam it straight down"] = "하드 드롭  —  단숨에 바닥까지 내리기",
        ["SPACE"] = "스페이스",
        ["⤓ button"] = "⤓ 버튼",
        ["HOLD  —  stash a piece for later"] = "홀드  —  블록을 잠시 보관하기",
        ["H button"] = "H 버튼",
        ["CLEAR A LINE  —  fill the row, drop into the gap"] = "라인 클리어  —  줄을 채우고 빈 칸에 넣기",
        ["fill the bottom row"] = "맨 아래 줄을 채우기",
        ["T-SPIN  —  rotate a T into the notch for bonus points"] = "T-스핀  —  T를 홈에 돌려 넣어 보너스 점수 얻기",
        ["spin the T (▲ / Z) into the slot"] = "T를 (▲ / Z) 돌려 홈에 넣기",
        ["spin the T (↻ / ↺) into the slot"] = "T를 (↻ / ↺) 돌려 홈에 넣기",
        ["DEFEND  —  clear a line to fight off incoming garbage"] = "방어  —  줄을 지워 들어오는 방해 블록 막기",
        ["fill the gap to clear a garbage row"] = "빈 칸을 채워 방해 줄을 지우기",
        ["ALL SET  —  you're ready to play!"] = "준비 완료  —  이제 플레이할 수 있어요!",
        ["SKIP"] = "건너뛰기",

        // ---- Descent (charm-draft gauntlet) --------------------------------
        ["DESCENT"] = "디센트",
        ["PLACE & SURVIVE · GARBAGE KEEPS RISING"] = "배치 서바이벌 · 밀려오는 가비지",
        ["FIVE STRATA · DRAFT CHARMS · GO DEEP"] = "다섯 지층 · 참 드래프트 · 더 깊이",
        ["DRAFT CHARMS, DIVE DEEP"] = "참을 모아 더 깊이 강하",
        ["DEPTH {0}/{1}"] = "깊이 {0}/{1}",
        ["NEXT: DEPTH {0}/{1}"] = "다음: 깊이 {0}/{1}",
        ["DIG"] = "굴착",
        ["BURST"] = "버스트",
        ["SIEGE"] = "공성",
        ["STRATUM {0} CLEARED!"] = "지층 {0} 돌파!",
        ["STRATUM {0}"] = "지층 {0}",
        ["BANKED {0}"] = "적립 {0}",
        ["CHOOSE A CHARM"] = "참 선택",
        ["SKIP (+{0} BANK)"] = "건너뛰기 (+{0} 적립)",
        ["OWNED: {0}"] = "보유: {0}",
        ["BOTTOM REACHED!"] = "최심부 도달!",
        ["RUN OVER"] = "런 종료",
        ["DEPTH"] = "깊이",
        ["BANKED SCORE"] = "적립 점수",
        ["DESCEND AGAIN"] = "다시 강하",
        ["CHARMS"] = "참",

        // Charm names (short display labels).
        ["GREED"] = "탐욕",
        ["PROPHET"] = "예언자",
        ["SPIN SAGE"] = "스핀 현자",
        ["ANCHOR"] = "닻",
        ["GAMBLER"] = "도박사",
        ["FEATHER"] = "깃털",
        ["BLINDFOLD"] = "눈가리개",
        ["MONK"] = "수도승",
        ["SAPPER"] = "공병",
        ["WARLORD"] = "군주",
        ["HOURGLASS"] = "모래시계",
        ["JUGGERNAUT"] = "거신",

        // Charm trade descriptions (reward - price).
        ["Score x1.5 - pieces fall 40% faster"] = "점수 ×1.5 — 블록 낙하 40% 빨라짐",
        ["+2 next previews - Hold is sealed"] = "NEXT 미리보기 +2 — 홀드 봉인",
        ["Every spin scores - lock delay 0.35s"] = "모든 스핀에 점수 — 락 딜레이 0.35초",
        ["Lock delay 0.8s - score x0.9"] = "락 딜레이 0.8초 — 점수 ×0.9",
        ["Score x2.0 - lock delay 0.15s"] = "점수 ×2.0 — 락 딜레이 0.15초",
        ["Gravity 60% slower - timed stages 15% shorter"] = "중력 60% 느려짐 — 시간제 지층 15% 짧아짐",
        ["No ghost piece - score x1.25"] = "고스트 표시 없음 — 점수 ×1.25",
        ["Hold is sealed - score x1.3"] = "홀드 봉인 — 점수 ×1.3",
        ["2 fewer garbage rows, slower rise - score x0.8"] = "가비지 2줄 감소·상승 둔화 — 점수 ×0.8",
        ["Siege goals 4 lines lighter - Burst 15s shorter"] = "공성 목표 4줄 감소 — 버스트 15초 짧아짐",
        ["Timed stages +20s - score x0.85"] = "시간제 지층 +20초 — 점수 ×0.85",
        ["Gravity 20% slower - siege goals +3"] = "중력 20% 느려짐 — 공성 목표 +3",
    };
}
