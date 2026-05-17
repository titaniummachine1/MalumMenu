using UnityEngine;

namespace MalumMenu;

public struct RadarMapConfig
{
    public string TextureName;
    public int TextureWidth;
    public int TextureHeight;
    public Vector2 BoundsMin;
    public Vector2 BoundsSize;
    public float PixelsPerUnit;
    public Rect UvRect;

    public RadarMapConfig(
        string textureName,
        int textureWidth,
        int textureHeight,
        Vector2 boundsMin,
        Vector2 boundsSize,
        float pixelsPerUnit,
        Rect uvRect)
    {
        TextureName = textureName;
        TextureWidth = textureWidth;
        TextureHeight = textureHeight;
        BoundsMin = boundsMin;
        BoundsSize = boundsSize;
        PixelsPerUnit = pixelsPerUnit;
        UvRect = uvRect;
    }

    public static RadarMapConfig[] GetDefaultConfigs()
    {
        return new RadarMapConfig[]
        {
            // Skeld - bounds calibrated from live Background renderer transform
            new RadarMapConfig(
                "map",
                980, 561,
                new Vector2(-5.44f, -4.06f),
                new Vector2(9.80f, 5.61f),
                100f,
                new Rect(0f, 0f, 1f, 1f)
            ),
            // Mira HQ - bounds calibrated from live Background renderer transform
            new RadarMapConfig(
                "map_HQ",
                760, 554,
                new Vector2(-2.15f, -0.78f),
                new Vector2(7.60f, 5.54f),
                100f,
                new Rect(0f, 0f, 1f, 1f)
            ),
            // Polus - bounds calibrated from live Background renderer transform
            new RadarMapConfig(
                "mapPB",
                832, 575,
                new Vector2(-0.01f, -5.325f),
                new Vector2(8.32f, 5.75f),
                100f,
                new Rect(0f, 0f, 1f, 1f)
            ),
            // Airship - bounds calibrated from live Background renderer transform
            new RadarMapConfig(
                "map4_airship",
                800, 425,
                new Vector2(-3.12f, -2.125f),
                new Vector2(8.00f, 4.25f),
                100f,
                new Rect(0f, 0f, 1f, 1f)
            ),
            // Fungle - bounds corrected for lossyScale=0.836 on MapBehaviour (sprite.bounds * lossyScale / _mapSpace.lossyScale)
            new RadarMapConfig(
                "FungleMinimap",
                944, 560,
                new Vector2(-4.598f, -2.783f),
                new Vector2(9.394f, 5.574f),
                100f,
                new Rect(0f, 0f, 1f, 1f)
            )
        };
    }
}
