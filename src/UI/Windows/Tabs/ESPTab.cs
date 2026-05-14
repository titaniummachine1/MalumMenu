using UnityEngine;

namespace MalumMenu;

public class ESPTab : ITab
{
    public string name => "ESP";

    private bool _mapShowExpanded;
    private bool _minimapShowExpanded;

    public void Draw()
    {
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(MenuUI.windowWidth * 0.425f));

        DrawGeneral();

        GUILayout.Space(15);

        DrawCamera();

        GUILayout.Space(15);

        DrawTracers();

        GUILayout.EndVertical();

        GUILayout.BeginVertical();

        DrawMap();

        GUILayout.Space(15);

        DrawMinimap();

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

    private void DrawMap()
    {
        GUILayout.Label("Map", GUIStylePreset.TabSubtitle);

        var mapArrow = _mapShowExpanded ? "▼" : "▶";
        if (GUILayout.Button($"{mapArrow} Show Options", GUIStylePreset.NormalButton))
        {
            _mapShowExpanded = !_mapShowExpanded;
        }

        if (_mapShowExpanded)
        {
            CheatToggles.mapCrew = GUILayout.Toggle(CheatToggles.mapCrew, " Crewmates");

            CheatToggles.mapImps = GUILayout.Toggle(CheatToggles.mapImps, " Impostors");

            CheatToggles.mapGhosts = GUILayout.Toggle(CheatToggles.mapGhosts, " Ghosts");

            CheatToggles.colorBasedMap = GUILayout.Toggle(CheatToggles.colorBasedMap, " Color-based");
        }
    }

    private void DrawMinimap()
    {
        GUILayout.Label("Minimap", GUIStylePreset.TabSubtitle);

        CheatToggles.minimapAlwaysOn = GUILayout.Toggle(CheatToggles.minimapAlwaysOn, " Enable Minimap");

        var minimapArrow = _minimapShowExpanded ? "▼" : "▶";
        if (GUILayout.Button($"{minimapArrow} Show Options", GUIStylePreset.NormalButton))
        {
            _minimapShowExpanded = !_minimapShowExpanded;
        }

        if (_minimapShowExpanded)
        {
            CheatToggles.minimapCrew = GUILayout.Toggle(CheatToggles.minimapCrew, " Crewmates");

            CheatToggles.minimapImps = GUILayout.Toggle(CheatToggles.minimapImps, " Impostors");

            CheatToggles.minimapGhosts = GUILayout.Toggle(CheatToggles.minimapGhosts, " Ghosts");

            CheatToggles.minimapColorBased = GUILayout.Toggle(CheatToggles.minimapColorBased, " Color-based");
        }

        CheatToggles.mapTrails = GUILayout.Toggle(CheatToggles.mapTrails, " Enable Trails");

        if (CheatToggles.mapTrails)
        {
            GUILayout.Label($" Trail Duration: {CheatToggles.mapTrailDuration:0}s");
            CheatToggles.mapTrailDuration = GUILayout.HorizontalSlider(CheatToggles.mapTrailDuration, 5f, 120f);
        }

        CheatToggles.minimapHideDuringMeeting = GUILayout.Toggle(CheatToggles.minimapHideDuringMeeting, " Hide During Meeting");

        CheatToggles.mapTrailsOnIngameMap = GUILayout.Toggle(CheatToggles.mapTrailsOnIngameMap, " Trails on Ingame Map");
    }
}
