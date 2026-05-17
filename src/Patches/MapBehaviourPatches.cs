using HarmonyLib;

namespace MalumMenu;

[HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.ShowNormalMap))]
public static class MapBehaviour_ShowNormalMap
{
    public static void Postfix(MapBehaviour __instance)
    {
        if (!CheatToggles.minimapAlwaysOn) return;

        __instance.ColorControl.SetColor(Palette.Purple);
        __instance.DisableTrackerOverlays();
    }
}
