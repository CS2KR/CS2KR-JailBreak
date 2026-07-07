using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Jailbreak.Config;
using Jailbreak.Features.Teams;
using Microsoft.Extensions.Logging;

namespace Jailbreak.Features.Beacon;

public sealed class BeaconManager
{
    private static readonly Color[] DefaultColors =
    [
        Color.FromArgb(255, 255, 51, 51),
        Color.FromArgb(255, 255, 204, 51),
        Color.FromArgb(255, 51, 255, 102),
        Color.FromArgb(255, 51, 204, 255),
        Color.FromArgb(255, 204, 102, 255)
    ];

    private readonly BasePlugin _plugin;
    private readonly BeaconConfig _config;
    private readonly ILogger _logger;
    private readonly List<CBeam> _activeBeams = new();
    private readonly Color[] _colors;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _timer;
    private ulong _prisonerSteamId;
    private ulong _guardSteamId;
    private int _drawTick;

    public BeaconManager(
        BasePlugin plugin,
        BeaconConfig config,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _plugin = plugin;
        _config = config;
        _logger = logger;
        _colors = ParseColors(config.Colors);
    }

    public bool IsActive => _timer is not null;

    public void StartOneVsOne(
        ulong prisonerSteamId,
        ulong guardSteamId)
    {
        if (!_config.Enabled ||
            prisonerSteamId == 0 ||
            guardSteamId == 0)
        {
            return;
        }

        if (_timer is not null &&
            _prisonerSteamId == prisonerSteamId &&
            _guardSteamId == guardSteamId)
        {
            return;
        }

        Stop();

        _prisonerSteamId = prisonerSteamId;
        _guardSteamId = guardSteamId;
        DrawBeacons();

        _timer = _plugin.AddTimer(
            Math.Clamp(_config.IntervalSeconds, 1.0f, 10.0f),
            DrawBeacons,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        _logger.LogInformation(
            "[Jailbreak] Beacon started for one-vs-one. PrisonerSteamID: {PrisonerSteamId}, GuardSteamID: {GuardSteamId}",
            prisonerSteamId,
            guardSteamId);
    }

    public void Stop()
    {
        _timer?.Kill();
        _timer = null;
        _prisonerSteamId = 0;
        _guardSteamId = 0;
        ClearBeams();
    }

    private void DrawBeacons()
    {
        ClearBeams();

        int colorOffset = _drawTick++;
        DrawBeaconForPlayer(_prisonerSteamId, colorOffset);
        DrawBeaconForPlayer(_guardSteamId, colorOffset);
    }

    private void DrawBeaconForPlayer(
        ulong steamId,
        int colorOffset)
    {
        CCSPlayerController? player = Utilities.GetPlayers()
            .FirstOrDefault(candidate =>
                TeamManager.IsHumanPlayer(candidate) &&
                candidate.SteamID == steamId &&
                candidate.PawnIsAlive);

        CCSPlayerPawn? pawn = player?.PlayerPawn.Value;

        if (pawn is null ||
            !pawn.IsValid ||
            pawn.AbsOrigin is null)
        {
            return;
        }

        Vector origin = pawn.AbsOrigin;
        float radius = Math.Clamp(_config.RadiusUnits, 32.0f, 256.0f);
        int segments = Math.Clamp(_config.SegmentCount, 8, 48);
        float z = origin.Z + _config.HeightOffset;

        for (int index = 0; index < segments; index++)
        {
            double startAngle = (Math.PI * 2.0 * index) / segments;
            double endAngle = (Math.PI * 2.0 * (index + 1)) / segments;

            Vector start = new(
                origin.X + (float)(Math.Cos(startAngle) * radius),
                origin.Y + (float)(Math.Sin(startAngle) * radius),
                z);
            Vector end = new(
                origin.X + (float)(Math.Cos(endAngle) * radius),
                origin.Y + (float)(Math.Sin(endAngle) * radius),
                z);

            Color color = _colors[(index + colorOffset) % _colors.Length];
            CreateBeamSegment(start, end, color);
        }

        PlayBeaconSound(pawn);
    }

    private void CreateBeamSegment(
        Vector start,
        Vector end,
        Color color)
    {
        CBeam? beam = Utilities.CreateEntityByName<CBeam>("beam");

        if (beam is null || !beam.IsValid)
        {
            return;
        }

        beam.Render = color;
        beam.Width = Math.Clamp(_config.Width, 1.0f, 8.0f);
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        beam.Teleport(start, new QAngle(), new Vector());
        beam.DispatchSpawn();
        _activeBeams.Add(beam);
    }

    private void PlayBeaconSound(CCSPlayerPawn pawn)
    {
        if (string.IsNullOrWhiteSpace(_config.Sound))
        {
            return;
        }

        try
        {
            pawn.EmitSound(_config.Sound);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "[Jailbreak] Beacon sound failed. Sound: {Sound}",
                _config.Sound);
        }
    }

    private void ClearBeams()
    {
        foreach (CBeam beam in _activeBeams)
        {
            if (!beam.IsValid)
            {
                continue;
            }

            try
            {
                beam.AcceptInput("Kill");
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    exception,
                    "[Jailbreak] Beacon beam cleanup failed.");
            }
        }

        _activeBeams.Clear();
    }

    private static Color[] ParseColors(IReadOnlyList<string>? colorTexts)
    {
        if (colorTexts is null || colorTexts.Count == 0)
        {
            return DefaultColors;
        }

        List<Color> colors = new();

        foreach (string colorText in colorTexts)
        {
            try
            {
                Color parsed = ColorTranslator.FromHtml(colorText);
                colors.Add(Color.FromArgb(255, parsed.R, parsed.G, parsed.B));
            }
            catch
            {
                // Invalid config entries are ignored; an empty result falls back below.
            }
        }

        return colors.Count == 0
            ? DefaultColors
            : colors.ToArray();
    }
}
