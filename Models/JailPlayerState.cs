using System.Drawing;

namespace Jailbreak.Models;

public sealed class JailPlayerState
{
    public JailPlayerState(ulong steamId)
    {
        SteamId = steamId;
    }

    public ulong SteamId { get; }

    public bool IsRebel { get; set; }

    public bool HasOriginalRenderColor { get; private set; }

    public Color OriginalRenderColor { get; private set; }

    public bool IsFreeday { get; set; }

    public void SaveOriginalRenderColor(Color color)
    {
        if (HasOriginalRenderColor)
        {
            return;
        }

        OriginalRenderColor = color;
        HasOriginalRenderColor = true;
    }

    public void ClearOriginalRenderColor()
    {
        OriginalRenderColor = default;
        HasOriginalRenderColor = false;
    }

    public void ResetRoundState()
    {
        IsRebel = false;
        IsFreeday = false;
        ClearOriginalRenderColor();
    }
}
