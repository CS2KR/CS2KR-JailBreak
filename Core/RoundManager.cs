using Jailbreak.Models;
using Microsoft.Extensions.Logging;

namespace Jailbreak.Core;

public sealed class RoundManager
{
    private readonly ILogger _logger;
    private readonly string _pluginInstanceId;

    public RoundManager(
        ILogger logger,
        string pluginInstanceId)
    {
        _logger = logger;
        _pluginInstanceId = pluginInstanceId;
    }

    public JailRoundState State { get; } = new();

    public void HandleMapStart(string mapName)
    {
        bool previousIsActive = State.IsActive;
        State.Reset(resetRoundNumber: true);
        State.SetMap(mapName);
        State.RecordStateChange(
            previousIsActive,
            "map_start reset",
            "map_start");

        _logger.LogInformation(
            "[CS2.KR] Map started. PluginInstance: {PluginInstance}, Map: {MapName}",
            _pluginInstanceId,
            mapName);
        LogStateChange();
    }

    public void HandleRoundStart()
    {
        bool previousIsActive = State.IsActive;
        bool wasAlreadyRecovered = State.IsActive;

        State.Reset();
        State.StartRound(
            incrementRoundNumber: !wasAlreadyRecovered);
        State.RecordStateChange(
            previousIsActive,
            "round_start event",
            "round_start");

        _logger.LogInformation(
            "[CS2.KR] Round started. PluginInstance: {PluginInstance}, Round: {RoundNumber}, Map: {MapName}, RecoveredBeforeEvent: {RecoveredBeforeEvent}",
            _pluginInstanceId,
            State.RoundNumber,
            State.CurrentMap,
            wasAlreadyRecovered);
        LogStateChange();
    }

    public bool RecoverActiveRound(
        string mapName,
        string reason,
        double elapsedSeconds = 0.0)
    {
        if (State.IsActive)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(mapName))
        {
            State.SetMap(mapName);
        }

        bool previousIsActive = State.IsActive;
        State.StartRound(
            elapsedSeconds: elapsedSeconds);
        State.RecordStateChange(
            previousIsActive,
            reason,
            "round_recovery");

        _logger.LogWarning(
            "[CS2.KR] Recovered active round state. PluginInstance: {PluginInstance}, Round: {RoundNumber}, Map: {MapName}, ElapsedSeconds: {ElapsedSeconds:F2}, Reason: {Reason}",
            _pluginInstanceId,
            State.RoundNumber,
            State.CurrentMap,
            elapsedSeconds,
            reason);
        LogStateChange();

        return true;
    }

    public void HandleRoundEnd()
    {
        if (!State.IsActive)
        {
            _logger.LogDebug(
                "[CS2.KR] Ignored round end because no round is active. PluginInstance: {PluginInstance}",
                _pluginInstanceId);
            return;
        }

        _logger.LogInformation(
            "[CS2.KR] Round ended. PluginInstance: {PluginInstance}, Round: {RoundNumber}, Map: {MapName}",
            _pluginInstanceId,
            State.RoundNumber,
            State.CurrentMap);

        bool previousIsActive = State.IsActive;
        State.EndRound();
        State.RecordStateChange(
            previousIsActive,
            "round_end event",
            "round_end");
        LogStateChange();
    }

    public void HandleMapEnd()
    {
        string mapName = State.CurrentMap;
        bool previousIsActive = State.IsActive;
        State.Reset(resetRoundNumber: true);
        State.SetMap(string.Empty);
        State.RecordStateChange(
            previousIsActive,
            "map_end reset",
            "map_end");

        _logger.LogInformation(
            "[CS2.KR] Map ended. PluginInstance: {PluginInstance}, Map: {MapName}",
            _pluginInstanceId,
            mapName);
        LogStateChange();
    }

    public void Shutdown()
    {
        bool previousIsActive = State.IsActive;
        State.Reset(resetRoundNumber: true);
        State.SetMap(string.Empty);
        State.RecordStateChange(
            previousIsActive,
            "plugin shutdown",
            "plugin_unload");

        LogStateChange();
    }

    private void LogStateChange()
    {
        _logger.LogInformation(
            "[CS2.KR] Round state changed. PluginInstance: {PluginInstance}, PreviousActive: {PreviousActive}, NewActive: {NewActive}, Reason: {Reason}, Event: {Event}, Map: {Map}, Round: {Round}, ChangedAtUtc: {ChangedAtUtc:O}",
            _pluginInstanceId,
            State.LastChangePreviousIsActive,
            State.LastChangeNewIsActive,
            State.LastChangeReason,
            State.LastChangeEventName,
            State.LastChangeMapName,
            State.LastChangeRoundNumber,
            State.LastChangeAtUtc);
    }
}
