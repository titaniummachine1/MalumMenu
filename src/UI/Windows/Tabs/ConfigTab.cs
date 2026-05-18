using UnityEngine;

namespace MalumMenu;

public class ConfigTab : ITab
{
    public string name => "Config";

    public void Draw()
    {
        GUILayout.BeginVertical(GUILayout.Width(MenuUI.windowWidth * 0.425f));

        DrawGeneral();

        GUILayout.EndVertical();
    }

    private void DrawGeneral()
    {
        CheatToggles.openConfig = GUILayout.Toggle(CheatToggles.openConfig, " Open Config");

        CheatToggles.reloadConfig = GUILayout.Toggle(CheatToggles.reloadConfig, " Reload Config");

        if (GUILayout.Button(" Save Config"))
        {
            // Manually trigger radar config save
            var radarWindow = Radar.GetWindow();
            if (radarWindow != null)
            {
                var s = Mathf.Clamp(Radar.scale, Radar.MinScale, Radar.MaxScale);
                var pos = Radar.anchoredPosition;
                MalumMenu.minimapScale.Value = s;
                MalumMenu.minimapPosX.Value = pos.x;
                MalumMenu.minimapPosY.Value = pos.y;
                MalumMenu.Plugin.Config.Save();
            }
        }

        CheatToggles.saveProfile = GUILayout.Toggle(CheatToggles.saveProfile, " Save to Profile");

        CheatToggles.loadProfile = GUILayout.Toggle(CheatToggles.loadProfile, " Load from Profile");
    }
}
