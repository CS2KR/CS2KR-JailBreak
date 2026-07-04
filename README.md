# CS2 Jailbreak Plugin 0.2.5

## 0.2.5 라운드 상태 진단본

이번 버전은 라운드 활성 상태를 성급하게 다시 판정하지 않고, 실제 서버 값을 수집하기 위한 진단 작업본입니다. 아직 운영 완료본이 아닙니다.

- `css_jb status`가 더 이상 라운드 상태를 자동 복구하거나 변경하지 않습니다.
- `Server.MapName`, `Server.CurrentTime`, `cs_gamerules` 프록시 수와 주요 `CCSGameRules` 원시값을 여러 줄로 출력합니다.
- `map_start`, `map_end`, `round_prestart`, `round_start`, `round_end` 수신 여부와 당시 GameRules 값을 서버 로그에 남깁니다.
- `round_prestart`와 `map_start`에서는 라운드 활성 상태를 강제로 복구하지 않습니다.
- 살아 있는 T/CT만 보고 라운드를 활성화하던 fallback을 제거했습니다.
- 복구는 맵 이름과 `cs_gamerules`가 존재하고, 기존 GameRules 조건이 활성 라운드처럼 보일 때만 시도합니다.
- 빈 `Localization/ko.json`을 제거하고 `lang/ko.json`, `lang/en.json`만 유지합니다.

### 진단 순서

먼저 Workshop 맵을 제외하고 기본 맵으로 서버를 실행합니다.

```bat
cs2.exe -dedicated -console -usercon -port 27015 -maxplayers 16 ^
+game_type 0 +game_mode 0 +map de_dust2 ^
+sv_lan 1 +sv_password test123 ^
+hostname "CS2 Jailbreak Test Server" ^
+exec server.cfg
```

서버 콘솔에서 다음 순서로 확인합니다.

```text
status
css_jb version
css_jb status
```

그다음 실제 접속과 라운드 진행 중 로그에서 아래 항목을 확보합니다.

```text
Listener received: map_start
Event received: round_prestart
Event received: round_start
Event received: round_end
GameRules diagnostic
```

이 로그를 기준으로 `Idle (console)`, 웜업, 프리즈 타임, 실제 라운드 진행 상태를 구분한 뒤 복구 조건을 다시 설계합니다.

## 0.2.4 간수 지시 HUD 유지 수정

- 시간 지정 지시가 마감 시각에 도달해도 중앙 HUD를 삭제하지 않습니다.
- 현재 지시는 새 지시로 교체되거나 직접 취소되거나 라운드가 끝날 때까지 유지됩니다.
- `7:30까지 이동`의 `7:30`은 지시 내용이며 HUD 만료 시각으로 사용하지 않습니다.
- 라운드 종료, 맵 종료, 플러그인 언로드 시에는 기존 지시를 정리합니다.

## 0.2.4 Center HUD hotfix

- Removed unsupported `div/style/font size` markup that could render as a blank white panel.
- Guard order HUD is now sent once with a duration matching the order lifetime instead of being resent every 0.5 seconds.
- The timer now only checks round/order state, new players, and menu-close restoration.
- HUD output is delayed by one frame after menu selection so the menu close blank message cannot erase it.


## 0.2.4 hotfix

- Fixed CS0165 in custom guard-order chat handling by separating the null-manager check from the `out string error` call.


Counter-Strike 2 감옥서버 게임 진행용 CounterStrikeSharp 플러그인입니다.

## 현재 목표

이번 버전은 신규 게임 추가보다 실제 사용성을 먼저 정리합니다.

1. 콘솔에만 보이던 메뉴를 게임 화면 중앙 메뉴로 변경
2. 간수 지시 HUD를 지시 종료 시점까지 계속 표시
3. 중복 명령어를 `css_jb` 하나로 통합
4. `!지시`, `!lr`, `!도움말`을 채팅에 노출하지 않고 처리
5. 자유 입력 제한시간과 취소 기능 추가
6. 관리자 권한을 설정 파일에서 변경 가능하게 통합
7. Localization 기본 구조 추가
8. 전체 자유시간 종료 후 LR이 다시 감지되도록 수정

## 수정 파일

```text
Jailbreak.csproj
JailbreakPlugin.cs
Jailbreak.example.json
Config/JailbreakConfig.cs
Features/Orders/GuardOrderManager.cs
Features/LastRequest/LastRequestManager.cs
Features/Freeday/FreedayManager.cs
lang/ko.json
lang/en.json
scripts/deploy.ps1
README.md
```

## 주요 변경 사항

### 화면 메뉴

기존 `ConsoleMenu`는 플레이어 콘솔에만 출력되므로 제거했습니다.

현재는 CounterStrikeSharp 기본 `CenterHtmlMenu`를 사용합니다.

```text
!지시
→ 화면 중앙에 장소 메뉴 표시
→ 채팅으로 !1, !2 등의 번호 선택
→ 시간 메뉴 표시
→ 번호 선택 후 메뉴 종료
```

LR 게임 및 상대 선택 메뉴도 같은 방식으로 화면 중앙에 표시됩니다.

기본 API만 사용하므로 외부 메뉴 플러그인은 필요하지 않습니다. 다만 실제 숫자키 또는 W/S/E 조작형 메뉴는 아니며, 차후 서버에 MenuManagerAPI를 설치할 수 있을 때 선택 기능으로 검토합니다.

### 간수 지시 HUD

시간 지시는 지정된 라운드 시간까지 계속 표시됩니다.

```text
[간수 지시]
샤워실로 7:30까지 이동
```

자유 입력 지시는 다음 조건까지 계속 표시됩니다.

```text
새 지시
관리자 취소
라운드 종료
맵 변경
플러그인 언로드
```

타이머는 `HudRefreshSeconds`마다 상태를 확인하지만, 같은 지시를 모든 플레이어에게 반복 전송하지 않습니다. 신규 접속자와 메뉴가 닫힌 플레이어에게 필요한 경우에만 다시 출력합니다. `HudPacketDurationSeconds`는 최소 CenterHtml 유지시간입니다.

메뉴가 열린 플레이어에게는 지시 HUD와 자유시간 HUD를 잠시 출력하지 않아 메뉴가 덮이지 않도록 했습니다. 메뉴를 닫으면 다음 갱신 때 HUD가 다시 표시됩니다.

### 자유 입력

```text
!지시
→ 자유 입력 선택
→ 제한시간 안에 채팅 한 줄 입력
```

입력 내용은 시간 파싱이나 문구 추가 없이 그대로 지시로 사용합니다.

```text
!취소
/cancel
```

위 명령으로 자유 입력 대기를 취소할 수 있습니다.

### 명령어

일반 플레이어:

```text
!지시
!lr
!도움말
```

관리 및 서버 콘솔:

```text
css_jb help
css_jb version
css_jb status

css_jb order
css_jb order cancel

css_jb lr
css_jb lr cancel

css_jb freeday start
css_jb freeday end
css_jb freeday give <대상>
css_jb freeday remove <대상>
css_jb freeday list

css_jb rebel add <대상>
css_jb rebel remove <대상>
css_jb rebel list
```

한국어 하위 명령도 일부 지원합니다.

```text
css_jb 상태
css_jb 지시 취소
css_jb 자유시간 시작
css_jb 반란 지정 <대상>
```

기존 `css_jbtest`, `css_jborder`, `css_jisi`, `css_jbfreeday` 등의 개별 명령은 더 이상 등록하지 않습니다.

## 관리자 권한

플러그인은 관리자 DB를 직접 만들지 않고 CounterStrikeSharp Admin Framework만 사용합니다.

기본 설정:

```json
"AdminPermissions": [
  "@jailbreak/admin"
]
```

CS2.KR 서버에서 이미 사용하는 권한을 그대로 이용하려면 `Jailbreak.json`에서 변경할 수 있습니다.

```json
"AdminPermissions": [
  "@css/generic",
  "@jailbreak/admin"
]
```

목록 중 하나라도 보유하면 관리자 기능을 사용할 수 있습니다.

대상 플레이어를 변경하는 반란자·개인 프리데이 명령은 CounterStrikeSharp 관리자 면역도도 확인합니다. 서버 콘솔은 모든 대상을 변경할 수 있습니다.

권한 그룹 예시:

```json
{
  "#jailbreak-admin": {
    "flags": [
      "@jailbreak/admin"
    ]
  }
}
```

실제 서버의 기존 관리자 구성에 맞춰 그룹 또는 권한만 추가하면 됩니다.

## Localization

플러그인 루트의 다음 폴더를 사용합니다.

```text
lang/ko.json
lang/en.json
```

현재 메뉴 핵심 문구부터 연결했습니다. 나머지 채팅·HUD 문구는 아직 한국어 고정 문구가 남아 있으며 다음 정리 단계에서 모두 키 기반으로 이전합니다.

CounterStrikeSharp 플레이어 언어 설정에 따라 지원되는 문구가 변경됩니다.

```text
!lang
css_lang ko
css_lang en
```

## 설정 예시

```json
{
  "ConfigVersion": 1,
  "PrisonersPerGuard": 3,
  "AdminPermissions": [
    "@jailbreak/admin"
  ],
  "CustomOrderInputTimeoutSeconds": 15,
  "GuardOrders": {
    "Enabled": true,
    "RoundDurationSeconds": 480,
    "TimeStepSeconds": 30,
    "TimeOptionCount": 5,
    "MinimumDeadlineSeconds": 30,
    "CommandCooldownSeconds": 3.0,
    "HudRefreshSeconds": 0.5,
    "HudPacketDurationSeconds": 2,
    "NotificationSound": "UIPanorama.popup_accept",
    "OrderTextFormat": "{location}로 {time}까지 이동",
    "DefaultLocations": [
      "중앙",
      "수영장",
      "샤워실",
      "운동장",
      "감방"
    ]
  }
}
```

## 변경 이유

- `ConsoleMenu`는 게임 화면 메뉴가 아니라 플레이어 콘솔 출력용입니다.
- 간수 지시는 잠깐 보이는 알림이 아니라 현재 라운드의 진행 규칙이므로 활성 상태 동안 계속 표시해야 합니다.
- 기능마다 관리자 명령을 따로 만들면 운영과 권한 관리가 복잡해집니다.
- 관리자 시스템을 직접 구현하면 CS2.KR 기존 관리자 플러그인과 충돌할 수 있습니다.
- 전체 자유시간을 시작할 때 LR을 해당 라운드에서 영구 종료하던 흐름을 제거했습니다.

## 빌드 방법

프로젝트 경로:

```powershell
cd D:\server\Projects\CS2-Jailbreak
```

Release 빌드:

```powershell
dotnet clean
dotnet build -c Release
```

완료 기준:

```text
Build succeeded.
0 Error(s)
```

## 배포 방법

```powershell
.\scripts\deploy.ps1
```

기본 배포 경로:

```text
D:\server\CS2Server\game\csgo\addons\counterstrikesharp\plugins\Jailbreak
```

배포 스크립트는 DLL, deps.json, PDB와 `lang` 폴더를 복사합니다.

## 테스트 절차

### 1. 로드 확인

```text
meta version
meta list
css_plugins list
css_jb version
css_jb status
```

### 2. 간수 지시 메뉴

1. 실제 라운드를 시작합니다.
2. 살아 있는 CT로 `!지시`를 입력합니다.
3. 콘솔이 아니라 게임 화면 중앙에 장소 메뉴가 표시되는지 확인합니다.
4. 채팅으로 `!1`을 입력합니다.
5. 화면 중앙에 시간 메뉴가 표시되는지 확인합니다.
6. 다시 `!1`을 입력합니다.
7. 메뉴가 종료되고 채팅·알림음·HUD가 출력되는지 확인합니다.

### 3. HUD 지속

1. 시간 지시를 내립니다.
2. HUD가 2초 후 사라지지 않고 계속 유지되는지 확인합니다.
3. 지정 시간이 지나면 자동으로 사라지는지 확인합니다.
4. `css_jb order cancel` 실행 시 즉시 사라지는지 확인합니다.
5. 라운드 종료 시 사라지는지 확인합니다.

### 4. 자유 입력

1. `!지시`에서 자유 입력을 선택합니다.
2. `30초 동안 앉기`를 입력합니다.
3. 문장이 가공되지 않고 그대로 표시되는지 확인합니다.
4. 새 지시 또는 라운드 종료 전까지 HUD가 유지되는지 확인합니다.
5. 제한시간 만료와 `!취소`가 정상 동작하는지 확인합니다.

### 5. 메뉴와 HUD 충돌

1. 기존 간수 지시 HUD가 표시된 상태에서 `!지시`를 다시 엽니다.
2. 메뉴가 HUD에 덮이지 않는지 확인합니다.
3. 메뉴 종료 후 기존 HUD가 다시 나타나는지 확인합니다.

### 6. 관리자 권한

1. 권한 없는 T가 관리자 하위 명령을 사용할 수 없는지 확인합니다.
2. 설정된 권한 보유자가 사용할 수 있는지 확인합니다.
3. 서버 콘솔에서 관리자 명령이 동작하는지 확인합니다.
4. 면역도가 높은 관리자를 낮은 관리자가 변경할 수 없는지 확인합니다.

### 7. 자유시간과 LR

1. 전체 자유시간을 시작합니다.
2. 전체 자유시간을 종료합니다.
3. 마지막 죄수 조건이 되면 LR이 다시 감지되는지 확인합니다.
4. LR 메뉴가 게임 화면 중앙에 표시되는지 확인합니다.

## 완료 조건

```text
화면 중앙 메뉴 표시
장소 → 시간 메뉴 전환
선택 후 메뉴 종료
간수 지시 HUD 상시 유지
시간 만료 시 자동 종료
자유 입력 그대로 출력
채팅 명령 숨김 처리
css_jb 단일 관리 명령 동작
설정 기반 관리자 권한 동작
lang 폴더 배포
전체 자유시간 종료 후 LR 재감지
라운드·맵 변경·언로드 시 타이머 정리
서버 콘솔 예외 없음
```

## 예상 오류

### 메뉴는 보이지만 선택되지 않음

현재 기본 메뉴 선택은 실제 숫자키가 아니라 채팅 `!1`부터 `!9`를 사용합니다. CounterStrikeSharp의 `css_1`부터 `css_9` 명령 등록 상태를 확인합니다.

### 메뉴와 HUD가 번갈아 깜빡임

다른 플러그인이 같은 Center HTML 영역을 반복 출력할 수 있습니다. 해당 플러그인과 HUD 출력 주기 또는 우선순위를 조정해야 합니다.

### 번역 파일을 찾지 못함

배포 경로에 다음 파일이 있는지 확인합니다.

```text
Jailbreak/lang/ko.json
Jailbreak/lang/en.json
```

### 관리자 권한이 동작하지 않음

`AdminPermissions`에 적힌 권한이 서버의 CounterStrikeSharp 관리자 데이터에 실제로 등록되어 있는지 확인합니다.

### 알림음이 들리지 않음

`NotificationSound`의 soundevent가 현재 CS2 버전에서 유효한지 확인합니다. 빈 문자열로 설정하면 소리를 비활성화할 수 있습니다.

### 라운드 시간이 실제 타이머와 맞지 않음

`RoundDurationSeconds`를 서버의 실제 라운드 시간과 동일하게 설정합니다.

## 아직 남은 작업

1. 실제 서버에서 CenterHtmlMenu 선택 흐름 확인
2. 모든 채팅·HUD 문구 Localization 이전
3. 핫 리로드 시 진행 중 라운드 상태 복원 방식 확정
4. LR 참가자 외 공격 차단
5. LR 시작 카운트다운 및 체력·방탄·탄약 초기화
6. 노스코프 줌 사용 실격 또는 강제 차단
7. 외부 ButtonMenu를 선택 의존성으로 지원할지 결정
8. 전체 실서버 안정화

## v0.2.4 라운드 상태 복구 기록

v0.2.4에서는 `cs_gamerules`와 살아 있는 플레이어 상태를 함께 사용해 복구를 시도했습니다. 실제 서버에서 원인이 확인되지 않은 상태로 조건이 늘어났고, 맵과 게임 월드가 없는 상태를 잘못 활성화할 가능성이 있어 v0.2.5에서 다음 항목을 중단했습니다.

- `css_jb status` 실행 중 상태 변경
- `map_start`, `round_prestart`에서 강제 복구
- 살아 있는 T/CT 기반 fallback

복구 시 라운드 경과시간을 반영하는 구조는 유지하지만, 최종 판정 조건은 v0.2.5 로그를 수집한 뒤 확정합니다.
