using UnityEngine;

namespace MalumMenu;

public class MinimapTab : ITab
{
    public string name => "Minimap";

    public void Draw()
    {
        GUILayout.Label("Window", GUIStylePreset.TabSubtitle);

        CheatToggles.minimapAlwaysOn = GUILayout.Toggle(CheatToggles.minimapAlwaysOn, " Enable Minimap Window");
        if (CheatToggles.minimapAlwaysOn)
        {
            var scale = Radar.scale;
            GUILayout.Label($" Scale: {scale:0.00}");
            Radar.scale = GUILayout.HorizontalSlider(scale, 0.15f, 0.75f);

            if (MalumMenu.minimapIconScale != null)
            {
                var iconScale = MalumMenu.minimapIconScale.Value;
                GUILayout.Label($" Icon Scale: {iconScale:0.00}");
                var newIconScale = GUILayout.HorizontalSlider(iconScale, 0.50f, 2.50f);
                if (Mathf.Abs(newIconScale - iconScale) > 0.0001f)
                {
                    MalumMenu.minimapIconScale.Value = newIconScale;
                    if (MalumMenu.Plugin != null) MalumMenu.Plugin.Config.Save();
                }
            }

            GUILayout.Label(" Drag the minimap to move it (while menu is open).");
            GUILayout.Label(" Open the in-game map once per match so the minimap background can initialize.");
            CheatToggles.minimapHideDuringMeeting = GUILayout.Toggle(CheatToggles.minimapHideDuringMeeting, " Hide During Meeting");
            CheatToggles.debugMinimap = GUILayout.Toggle(CheatToggles.debugMinimap, " Debug Minimap (Log)");
        }

        GUILayout.Space(10);

        GUILayout.Label("Players", GUIStylePreset.TabSubtitle);

        CheatToggles.mapCrew = GUILayout.Toggle(CheatToggles.mapCrew, " Crewmates");
        CheatToggles.mapImps = GUILayout.Toggle(CheatToggles.mapImps, " Impostors");
        CheatToggles.mapGhosts = GUILayout.Toggle(CheatToggles.mapGhosts, " Ghosts");
        CheatToggles.colorBasedMap = GUILayout.Toggle(CheatToggles.colorBasedMap, " Color-based");

        GUILayout.Space(10);

        GUILayout.Label("Extras", GUIStylePreset.TabSubtitle);

        CheatToggles.mapImpsHighlight = GUILayout.Toggle(CheatToggles.mapImpsHighlight, " Highlight Impostors (Red)");
        CheatToggles.minimapDoorSabotage = GUILayout.Toggle(CheatToggles.minimapDoorSabotage, " Door Sabotage (Right-Click)");

        CheatToggles.mapTrails = GUILayout.Toggle(CheatToggles.mapTrails, " Player Trails");
        if (CheatToggles.mapTrails)
        {
            var seconds = MinimapHandler.trailSeconds;
            GUILayout.Label($" Trail Duration: {seconds:0}s");
            var newSeconds = GUILayout.HorizontalSlider(seconds, 5f, 60f);
            if (Mathf.Abs(newSeconds - seconds) > 0.001f)
            {
                MinimapHandler.trailSeconds = newSeconds;
            }
        }
    }
}
