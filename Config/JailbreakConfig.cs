using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace Jailbreak.Config;

public sealed class JailbreakConfig : BasePluginConfig
{
    [JsonPropertyName("PrisonersPerGuard")]
    public int PrisonersPerGuard { get; set; } = 3;

    [JsonPropertyName("AdminPermissions")]
    public List<string> AdminPermissions { get; set; } =
    [
        "@jailbreak/admin"
    ];

    [JsonPropertyName("CustomOrderInputTimeoutSeconds")]
    public int CustomOrderInputTimeoutSeconds { get; set; } = 15;

    [JsonPropertyName("DisableRadar")]
    public bool DisableRadar { get; set; } = true;

    [JsonPropertyName("DisableRadarCommand")]
    public string DisableRadarCommand { get; set; } = "sv_disable_radar 1";

    [JsonPropertyName("BotsBlockLastRequest")]
    public bool BotsBlockLastRequest { get; set; } = true;

    [JsonPropertyName("GuardOrders")]
    public GuardOrderConfig GuardOrders { get; set; } = new();
}

public sealed class GuardOrderConfig
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    // 서버의 실제 라운드 시간에 맞춰 설정합니다. 기본값: 8분
    [JsonPropertyName("RoundDurationSeconds")]
    public int RoundDurationSeconds { get; set; } = 480;

    // 시간 선택 간격입니다. 30이면 7:30, 7:00, 6:30 순서로 생성됩니다.
    [JsonPropertyName("TimeStepSeconds")]
    public int TimeStepSeconds { get; set; } = 30;

    [JsonPropertyName("TimeOptionCount")]
    public int TimeOptionCount { get; set; } = 5;

    [JsonPropertyName("MinimumDeadlineSeconds")]
    public int MinimumDeadlineSeconds { get; set; } = 30;

    [JsonPropertyName("CommandCooldownSeconds")]
    public float CommandCooldownSeconds { get; set; } = 3.0f;

    // 활성 지시 상태, 신규 접속, 메뉴 종료 후 HUD 복원을 확인하는 간격입니다.
    [JsonPropertyName("HudRefreshSeconds")]
    public float HudRefreshSeconds { get; set; } = 0.5f;

    // 최소 CenterHtml 유지시간입니다. 실제 출력은 지시 종료 시각까지 자동 연장됩니다.
    [JsonPropertyName("HudPacketDurationSeconds")]
    public int HudPacketDurationSeconds { get; set; } = 2;

    // CS2 soundevent 이름입니다. 빈 문자열이면 소리를 재생하지 않습니다.
    [JsonPropertyName("NotificationSound")]
    public string NotificationSound { get; set; } = "UIPanorama.popup_accept";

    [JsonPropertyName("OrderTextFormat")]
    public string OrderTextFormat { get; set; } =
        "{location}로 {time}까지 이동";

    [JsonPropertyName("DefaultLocations")]
    public List<string> DefaultLocations { get; set; } =
    [
        "중앙",
        "수영장",
        "샤워실",
        "운동장"
    ];
}
