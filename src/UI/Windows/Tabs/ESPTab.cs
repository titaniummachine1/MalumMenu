using UnityEngine;

namespace MalumMenu;

public class ESPTab : ITab
{
    public string name => "ESP";

    public void Draw()
    {
        GUILayout.BeginHorizontal();

        // Left column: General, Camera, Tracers
        GUILayout.BeginVertical(GUILayout.Width(MenuUI.windowWidth * 0.425f));

        DrawGeneral();

        GUILayout.Space(15);

        DrawCamera();

        GUILayout.Space(15);

        DrawTracers();

        GUILayout.EndVertical();

        // Right column: Radar
        GUILayout.BeginVertical();

        DrawRadar();

        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    private void DrawGeneral()
    {
        CheatToggles.seePlayerInfo = GUILayout.Toggle(CheatToggles.seePlayerInfo, " See Player Info");

        CheatToggles.seeRoles = GUILayout.Toggle(CheatToggles.seeRoles, " See Roles");

        CheatToggles.seeGhosts = GUILayout.Toggle(CheatToggles.seeGhosts, " See Ghosts");

        CheatToggles.noShadows = GUILayout.Toggle(CheatToggles.noShadows, " No Shadows");

        CheatToggles.taskArrows = GUILayout.Toggle(CheatToggles.taskArrows, " Task Arrows");

        CheatToggles.revealVotes = GUILayout.Toggle(CheatToggles.revealVotes, " Reveal Votes");

        CheatToggles.seeLobbyInfo = GUILayout.Toggle(CheatToggles.seeLobbyInfo, " See Lobby Info");
    }

    private void DrawCamera()
    {
        GUILayout.Label("Camera", GUIStylePreset.TabSubtitle);

        CheatToggles.zoomOut = GUILayout.Toggle(CheatToggles.zoomOut, " Zoom Out");

        CheatToggles.spectate = GUILayout.Toggle(CheatToggles.spectate, " Spectate");

        CheatToggles.freecam = GUILayout.Toggle(CheatToggles.freecam, " Freecam");
    }

    private void DrawTracers()
    {
        GUILayout.Label("Tracers", GUIStylePreset.TabSubtitle);

        CheatToggles.tracersCrew = GUILayout.Toggle(CheatToggles.tracersCrew, " Crewmates");

        CheatToggles.tracersImps = GUILayout.Toggle(CheatToggles.tracersImps, " Impostors");

        CheatToggles.tracersGhosts = GUILayout.Toggle(CheatToggles.tracersGhosts, " Ghosts");

        CheatToggles.tracersBodies = GUILayout.Toggle(CheatToggles.tracersBodies, " Dead Bodies");

        CheatToggles.colorBasedTracers = GUILayout.Toggle(CheatToggles.colorBasedTracers, " Color-based");

        CheatToggles.distanceBasedTracers = GUILayout.Toggle(CheatToggles.distanceBasedTracers, " Distance-based");
    }

    private void DrawRadar()
    {
        GUILayout.Label("Radar", GUIStylePreset.TabSubtitle);

        CheatToggles.minimapAlwaysOn = GUILayout.Toggle(CheatToggles.minimapAlwaysOn, " Enable Radar");

        if (CheatToggles.minimapAlwaysOn)
        {
            CheatToggles.radarCrew = GUILayout.Toggle(CheatToggles.radarCrew, " Crewmates");

            CheatToggles.radarImps = GUILayout.Toggle(CheatToggles.radarImps, " Impostors");

            CheatToggles.radarGhosts = GUILayout.Toggle(CheatToggles.radarGhosts, " Ghosts");

            CheatToggles.radarColorBased = GUILayout.Toggle(CheatToggles.radarColorBased, " Color-based");

            CheatToggles.mapTrails = GUILayout.Toggle(CheatToggles.mapTrails, " Show Trails");

            if (CheatToggles.mapTrails)
            {
                GUILayout.Label($"Trail Duration: {CheatToggles.mapTrailDuration:F0}s", GUIStylePreset.TabSubtitle);
                CheatToggles.mapTrailDuration = GUILayout.HorizontalSlider(CheatToggles.mapTrailDuration, 5f, 60f);
            }
        }

        CheatToggles.minimapHideDuringMeeting = GUILayout.Toggle(CheatToggles.minimapHideDuringMeeting, " Hide During Meeting");

        var offset = Mathf.RoundToInt(Radar.scaleOffsetPercent);
        GUILayout.Label($"Size: {(offset >= 0 ? "+" : "")}{offset}%", GUIStylePreset.TabSubtitle);
        Radar.scaleOffsetPercent = GUILayout.HorizontalSlider(Radar.scaleOffsetPercent, -40f, 200f, GUILayout.Width(MenuUI.windowWidth * 0.25f));
        Radar.scale = Mathf.Clamp(Radar.baseScale * (1f + (Radar.scaleOffsetPercent - 80f) / 100f), Radar.MinScale, Radar.MaxScale);
    }

}
