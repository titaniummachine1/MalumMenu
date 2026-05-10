using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MalumMenu;

internal sealed class RadarTrail
{
    public readonly Vector2[] points;
    public readonly float[] times;
    public int start;
    public int count;
    public float nextRecordTime;
    public Vector2 headPoint;
    public bool hasHeadPoint;
    public readonly List<Image> segments;
    public readonly List<SpriteRenderer> mapSegments;
    public Color color;

    public RadarTrail(int maxWaypoints)
    {
        points = new Vector2[maxWaypoints];
        times = new float[maxWaypoints];
        segments = new List<Image>(maxWaypoints);
        mapSegments = new List<SpriteRenderer>(maxWaypoints);
    }
}

internal sealed class RadarPlayerUi
{
    public byte id;
    public Image highlight;
    public Image dot;
    public float lastDotSize;
    public float lastHighlightSize;
    public RadarTrail trail;
}
