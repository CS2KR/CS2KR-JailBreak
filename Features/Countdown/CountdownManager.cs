using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Jailbreak.Features.Teams;
using Microsoft.Extensions.Logging;

namespace Jailbreak.Features.Countdown;

public sealed class CountdownManager
{
    private readonly BasePlugin _plugin;
    private readonly ILogger _logger;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _timer;
    private int _remainingSeconds;
    private string _label = string.Empty;

    public CountdownManager(
        BasePlugin plugin,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(logger);

        _plugin = plugin;
        _logger = logger;
    }

    public bool IsActive => _timer is not null;

    public string StatusText =>
        IsActive
            ? $"{_remainingSeconds}초 남음"
            : "없음";

    public bool Start(
        CCSPlayerController issuer,
        int seconds,
        string label,
        out string error)
    {
        error = string.Empty;

        if (seconds < 1 || seconds > 10)
        {
            error = "카운트다운은 1~10초만 사용할 수 있습니다.";
            return false;
        }

        Stop();

        _remainingSeconds = seconds;
        _label = string.IsNullOrWhiteSpace(label)
            ? "시작"
            : label.Trim();

        Broadcast($" CS2.\x0BKR\x01｜ {issuer.PlayerName}의 {_remainingSeconds}초 카운트다운: {_label}");
        ShowCountdown();

        _timer = _plugin.AddTimer(
            1.0f,
            Tick,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        _logger.LogInformation(
            "[CS2.KR] Countdown started. Issuer: {Issuer}, SteamID: {SteamID}, Seconds: {Seconds}, Label: {Label}",
            issuer.PlayerName,
            issuer.SteamID,
            seconds,
            _label);

        return true;
    }

    public void Stop()
    {
        _timer?.Kill();
        _timer = null;
        _remainingSeconds = 0;
        _label = string.Empty;
    }

    private void Tick()
    {
        _remainingSeconds--;

        if (_remainingSeconds <= 0)
        {
            Broadcast($" CS2.\x0BKR\x01｜ 카운트다운 종료: {_label}");
            PrintCenterAll($"CS2.KR｜\n{_label}");
            Stop();
            return;
        }

        ShowCountdown();
    }

    private void ShowCountdown()
    {
        PrintCenterAll($"CS2.KR｜\n{_remainingSeconds}");
        Broadcast($"[CS2.KR] {_remainingSeconds}");
    }

    private static void PrintCenterAll(string message)
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (TeamManager.IsHumanPlayer(player))
            {
                player.PrintToCenter(message);
            }
        }
    }

    private static void Broadcast(string message)
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (TeamManager.IsHumanPlayer(player))
            {
                player.PrintToChat(message);
            }
        }
    }
}
