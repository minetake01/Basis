using UnityEngine;

/// <summary>GPU helpers for Spout-compatible texture preparation.</summary>
public static class BasisMediaStreamGraphics
{
    /// <summary>Copies <paramref name="source"/> into <paramref name="dest"/> with a vertical flip (Unity → Spout/OBS).</summary>
    public static void BlitFlipVertical(Texture source, RenderTexture dest)
    {
        Graphics.Blit(source, dest, new Vector2(1f, -1f), new Vector2(0f, 1f));
    }
}
