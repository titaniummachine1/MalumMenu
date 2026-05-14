using System.Collections.Generic;
using UnityEngine;

namespace MalumMenu;
public static class MinimapHandler
{
    public static bool minimapActive;
    public static List<HerePoint> herePoints = new List<HerePoint>();
    public static List<HerePoint> herePointsToRemove = new List<HerePoint>();

    private static readonly Dictionary<byte, List<Vector3>> _trails = new();
    private static readonly Dictionary<byte, List<float>> _trailTimes = new();

    public static bool IsCheatEnabled()
    {
        if (CheatToggles.minimapHideDuringMeeting && Utils.isMeeting) return false;
        return CheatToggles.mapCrew || CheatToggles.mapGhosts || CheatToggles.mapImps
            || CheatToggles.minimapCrew || CheatToggles.minimapGhosts || CheatToggles.minimapImps;
    }

    public static void RecordTrailPoints()
    {
        if (!CheatToggles.mapTrails && !CheatToggles.mapTrailsOnIngameMap) return;
        if (!Utils.isShip || Utils.isMeeting) return;

        var now = Time.time;
        var maxAge = Mathf.Max(CheatToggles.mapTrailDuration, 5f);

        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (player == null || player.Data == null) continue;

            var id = player.PlayerId;
            if (!_trails.ContainsKey(id))
            {
                _trails[id] = new List<Vector3>();
                _trailTimes[id] = new List<float>();
            }

            var pos = player.transform.position;
            var trail = _trails[id];
            var times = _trailTimes[id];

            if (trail.Count == 0 || Vector3.Distance(trail[trail.Count - 1], pos) > 0.15f)
            {
                trail.Add(pos);
                times.Add(now);
            }

            while (times.Count > 0 && now - times[0] > maxAge)
            {
                trail.RemoveAt(0);
                times.RemoveAt(0);
            }
        }
    }

    public static void ClearTrails()
    {
        _trails.Clear();
        _trailTimes.Clear();
    }

    public static Dictionary<byte, List<Vector3>> GetTrails() => _trails;

    private static readonly Dictionary<byte, LineRenderer> _trailRenderers = new();

    public static void RenderTrailsOnMap(MapBehaviour map)
    {
        if (!CheatToggles.mapTrailsOnIngameMap || map == null || ShipStatus.Instance == null) return;

        var parent = map.HerePoint?.transform?.parent;
        if (parent == null) return;

        var mapScale = ShipStatus.Instance.MapScale;
        var mapSign = Mathf.Sign(ShipStatus.Instance.transform.localScale.x);

        foreach (var kvp in _trails)
        {
            var id = kvp.Key;
            var points = kvp.Value;
            if (points.Count < 2) continue;

            if (!_trailRenderers.TryGetValue(id, out var lr) || lr == null)
            {
                var go = new GameObject($"MalumTrail_{id}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
                lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.widthMultiplier = 0.03f;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 2;
                lr.material = map.HerePoint.material;
                lr.startColor = Color.white;
                lr.endColor = Color.white;
                _trailRenderers[id] = lr;
            }

            lr.positionCount = points.Count;
            for (var i = 0; i < points.Count; i++)
            {
                var v = points[i];
                v /= mapScale;
                v.x *= mapSign;
                v.z = -0.5f;
                lr.SetPosition(i, v);
            }
        }

        foreach (var id in new List<byte>(_trailRenderers.Keys))
        {
            if (!_trails.ContainsKey(id) && _trailRenderers.TryGetValue(id, out var lr) && lr != null)
            {
                Object.Destroy(lr.gameObject);
                _trailRenderers.Remove(id);
            }
        }
    }

    public static void DestroyTrailRenderers()
    {
        foreach (var lr in _trailRenderers.Values)
        {
            if (lr != null) Object.Destroy(lr.gameObject);
        }
        _trailRenderers.Clear();
    }

    public static void HandleHerePoint(HerePoint herePoint)
    {
        Color herePointColor = new Color();

        try // try-catch to fix issues caused by player disconnection
        {
            herePoint.sprite.gameObject.SetActive(false); // Initally make player icon invisible

            // Crewmate, alive
            if (CheatToggles.minimapCrew && !herePoint.player.Data.Role.IsImpostor)
            {
                if (!herePoint.player.Data.IsDead)
                {
                    herePoint.sprite.gameObject.SetActive(true);
                    if (CheatToggles.minimapColorBased)
                    {
                        herePointColor = herePoint.player.Data.Color; // Color-Based Icon
                    }
                    else
                    {
                        herePointColor = herePoint.player.Data.Role.TeamColor; // Role-Based Icon
                    }
                }
            }
            // Impostor, alive
            else if (CheatToggles.minimapImps && herePoint.player.Data.Role.IsImpostor)
            {
                if (!herePoint.player.Data.IsDead)
                {
                    herePoint.sprite.gameObject.SetActive(true);
                    if (CheatToggles.minimapColorBased)
                    {
                        herePointColor = herePoint.player.Data.Color; // Color-Based Icon
                    }
                    else
                    {
                        herePointColor = herePoint.player.Data.Role.TeamColor; // Role-Based Icon
                    }
                }
            }
            // Any Role, dead
            if (CheatToggles.minimapGhosts && herePoint.player.Data.IsDead)
            {
                herePoint.sprite.gameObject.SetActive(true);
                if (CheatToggles.minimapColorBased)
                {
                    herePointColor = herePoint.player.Data.Color; // Color-Based Icon
                }
                else
                {
                    herePointColor = Palette.White;
                }
            }

            if (herePoint.sprite.gameObject.active)
            {
                // Set the right colors for active herePoint icons
                herePoint.sprite.material.SetColor(PlayerMaterial.BackColor, herePointColor);
                herePoint.sprite.material.SetColor(PlayerMaterial.BodyColor, herePointColor);
                herePoint.sprite.material.SetColor(PlayerMaterial.VisorColor, Palette.VisorColor);

                // Sync the position of active herePoint icons with their players
                var vector = herePoint.player.transform.position;
                vector /= ShipStatus.Instance.MapScale;
                vector.x *= Mathf.Sign(ShipStatus.Instance.transform.localScale.x);
                vector.z = -1f;
                herePoint.sprite.transform.localPosition = vector;
            }
        }
        catch
        {
            // Remove icons that are causing problems
            Object.Destroy(herePoint.sprite.gameObject);
            herePointsToRemove.Add(herePoint);
        }
    }
}
