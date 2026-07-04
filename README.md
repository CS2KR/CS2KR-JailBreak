# CS2 Jailbreak Plugin

CounterStrikeSharp 기반 CS2 감옥서버 진행 보조 플러그인입니다.

현재 버전은 `0.2.6`이며, 실서버 테스트를 거치며 간수 지시, 자유시간, 반란자, LR, 봇 판정, 라운드 상태 진단을 안정화하는 단계입니다.

## 주요 기능

- 간수 지시 메뉴와 HUD
- 자유 입력 지시
- 전체/개인 자유시간
- 반란자 지정과 자동 반란 처리
- 마지막 죄수 LR
- 칼전, 권총전, 노스코프전
- LR 외부 개입 데미지 차단
- 간수 비율 제한
- 봇 참가자 판정 분리
- 관리자 상태 확인과 강제 초기화
- 미니맵 비활성화 시도

## 현재 운영 정책

### 봇 판정

- 봇은 게임 참가자로 취급합니다.
- 봇 죄수가 살아 있으면 기본적으로 LR을 막습니다.
- 봇은 간수 비율 계산에서는 제외합니다.
- 봇 간수는 LR 상대가 될 수 있습니다.

테스트 목적으로 봇 죄수를 무시하고 LR을 열고 싶다면 `BotsBlockLastRequest`를 `false`로 바꿀 수 있습니다. 운영 기본값은 `true`입니다.

### 간수 지시

- `!지시`, `/지시`, `!order`, `/order`로 열 수 있습니다.
- 관리자 권한자가 아니면 CT 간수만 지시할 수 있습니다.
- T 상태에서는 지시할 수 없습니다.
- 살아있음 여부는 보지 않습니다. CS2에서 `PawnIsAlive` 판정이 순간적으로 흔들려 지시가 막히던 문제가 있었기 때문입니다.
- LR 진행 중에는 새 간수 지시를 막습니다.

### 간수 지시 HUD

간수 지시는 중앙 텍스트로 반복 출력됩니다.

```text
[간수 지시]
중앙로 6:30까지 이동
```

CS2의 `PrintToCenterHtml`은 환경에 따라 흰 박스만 표시되거나 깜빡일 수 있어 현재는 일반 `PrintToCenter`를 사용합니다.

### 지시 메뉴

기본 장소 메뉴는 첫 페이지에 4개 장소와 자유 입력을 배치합니다.

```text
1. 중앙
2. 수영장
3. 샤워실
4. 운동장
5. 자유 입력
```

`감방`은 메뉴에서 숨깁니다. 페이지 넘김 실수로 다른 지시를 내리는 상황을 줄이기 위해 자유 입력을 첫 페이지 5번에 둡니다.

### LR

- 살아 있는 LR 차단 대상 죄수가 1명일 때 마지막 죄수로 감지합니다.
- 기본 설정에서는 봇 죄수도 LR 차단 대상입니다.
- 마지막 죄수가 `!lr`을 입력하면 메뉴를 엽니다.
- 자동 감지가 늦어도 `!lr` 입력 시 조건이 맞으면 후보를 복구합니다.
- LR 참가자 외 데미지는 차단합니다.
- 칼전에서 총 데미지는 차단합니다.
- 서버 안정성을 위해 LR 중 반복 무기 제거는 하지 않습니다.
- LR 시작 시에만 무기를 정리하고 지급합니다.

현재 구현된 게임:

- 칼전
- 권총전
- 노스코프전

### 반란자

- 죄수가 간수를 공격하면 반란자로 표시됩니다.
- 반란자는 빨간색 렌더 컬러를 적용합니다.
- 스킨 플러그인이 색을 덮어쓰는 경우를 줄이기 위해 1초마다 반란자 색을 다시 적용합니다.
- 라운드 종료, 팀 변경, 퇴장, 초기화 때 상태를 정리합니다.

### 미니맵

플러그인 로드와 맵 시작 시 설정된 서버 명령을 실행합니다.

기본값:

```text
sv_disable_radar 1
```

CS2 버전이나 서버 설정에 따라 해당 cvar가 동작하지 않을 수 있습니다. 실패 시 서버 로그에 경고만 남깁니다.

## 명령어

플레이어 채팅:

```text
!지시
!lr
!도움말
```

관리 명령:

```text
css_jb help
css_jb version
css_jb status
css_jb reset

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

한국어 하위 명령 일부도 지원합니다.

```text
css_jb 상태
css_jb 초기화
css_jb 지시 취소
css_jb 자유시간 시작
css_jb 반란 지정 <대상>
```

## 설정 예시

`Jailbreak.example.json`:

```json
{
  "ConfigVersion": 1,
  "PrisonersPerGuard": 3,
  "AdminPermissions": [
    "@jailbreak/admin"
  ],
  "CustomOrderInputTimeoutSeconds": 15,
  "DisableRadar": true,
  "DisableRadarCommand": "sv_disable_radar 1",
  "BotsBlockLastRequest": true,
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
      "운동장"
    ]
  }
}
```

### 주요 설정

`PrisonersPerGuard`
: 간수 1명당 필요한 죄수 수입니다. 봇은 비율 계산에서 제외됩니다.

`AdminPermissions`
: 관리자 권한 목록입니다. 하나라도 보유하면 관리자 기능을 사용할 수 있습니다.

`DisableRadar`
: 플러그인 로드/맵 시작 때 레이더 비활성화 명령을 실행할지 여부입니다.

`DisableRadarCommand`
: 레이더 비활성화에 사용할 서버 명령입니다.

`BotsBlockLastRequest`
: `true`면 봇 죄수도 LR을 막습니다. 운영 기본값은 `true`입니다.

`GuardOrders.DefaultLocations`
: 간수 지시 장소 목록입니다. 메뉴에는 최대 4개가 표시되고 5번은 자유 입력입니다.

## 빌드

```powershell
cd D:\server\Projects\CS2-Jailbreak
dotnet clean
dotnet build -c Release
```

빌드 산출물:

```text
bin\Release\net10.0
```

## 배포

서버가 실행 중일 때 DLL을 덮어쓰면 CounterStrikeSharp hot reload 상태가 꼬일 수 있습니다. 예전 코드와 새 코드가 섞여 보이는 증상이 생길 수 있으므로 기본 흐름은 아래처럼 사용합니다.

서버 실행 중에는 빌드만:

```powershell
.\scripts\deploy.ps1 -BuildOnly
```

서버를 끈 뒤 배포:

```powershell
.\scripts\deploy.ps1
```

기본 배포 경로:

```text
D:\server\CS2Server\game\csgo\addons\counterstrikesharp\plugins\Jailbreak
```

정말 실행 중인 서버에 강제 복사해야 한다면:

```powershell
.\scripts\deploy.ps1 -ForceLive
```

권장하지 않습니다. 강제 복사 후에는 서버 재시작 또는 완전한 플러그인 unload/load를 권장합니다.

## 배포 확인

서버 콘솔 또는 게임 내에서 확인합니다.

```text
meta version
meta list
css_plugins list
css_jb version
css_jb status
```

`css_jb version`은 버전, 플러그인 인스턴스 ID, 모듈 경로를 출력합니다. 새 DLL이 적용됐는지 확인할 때 먼저 봅니다.

## 테스트 체크리스트

### 간수 지시

- T 상태에서 `!지시`가 막히는지 확인
- CT 상태에서 `!지시`가 열리는지 확인
- LR 중 새 지시가 막히는지 확인
- 자유 입력이 5번에 있는지 확인
- 새 지시, 라운드 종료, 맵 변경 때 기존 HUD가 정리되는지 확인

### LR

- 봇 죄수가 살아 있으면 `BotsBlockLastRequest=true`에서 LR이 막히는지 확인
- `aliveGameplayT=1`이고 그 1명이 인간이면 `!lr`이 열리는지 확인
- 봇 간수를 상대로 LR을 시작할 수 있는지 확인
- 칼전에서 총 데미지가 막히는지 확인
- 칼전에서 칼 데미지가 정상적으로 들어가는지 확인
- LR 중 서버가 꺼지지 않는지 확인

### 반란자

- 죄수가 CT를 공격하면 빨간색으로 바뀌는지 확인
- 스킨 플러그인 적용 후에도 빨간색이 유지되는지 확인
- 라운드 종료/팀 변경/퇴장 때 원래 색으로 복구되는지 확인

### 상태 초기화

- `css_jb status`가 라운드 상태를 바꾸지 않고 진단만 출력하는지 확인
- `css_jb reset`이 자유시간, 반란자, LR, 간수 지시 상태를 초기화하는지 확인

## 알려진 제약

- CS2 서버 플러그인만으로 우측 중앙 고정 HUD를 안정적으로 만드는 방법은 없습니다.
- CS:S 스타일 MOTD 웹페이지는 CS2에서 기대하기 어렵습니다.
- `PrintToCenterHtml`은 흰 박스나 깜빡임이 발생할 수 있어 현재 사용하지 않습니다.
- LR 중 금지 무기를 실제로 제거하지 않습니다. 서버 안정성을 위해 데미지 차단 방식으로 처리합니다.
- `sv_disable_radar 1`은 서버/CS2 버전에 따라 동작하지 않을 수 있습니다.
- 라디오 메뉴 형태의 지시 UI는 아직 구현하지 않았습니다.

## 남은 작업

- 실제 서버에서 LR 칼전 데미지와 크래시 여부 재검증
- 권총전/노스코프전 장시간 테스트
- 지시 문구 config화
- 채팅/HUD 하드코딩 문구 Localization 정리
- 서버 규칙 안내 메뉴 또는 접속 안내 구현
- 스킨 플러그인과 반란자 색 충돌 확인
- 라디오 메뉴 또는 더 안정적인 UI 대안 조사
