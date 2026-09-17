using HarmonyLib;
using ResoniteModLoader;

namespace ProximityGrabCustomGesture;

public class ProximityGrabCustomGestureMod : ResoniteMod
{
    internal const string VERSION_CONSTANT = "0.2.0";

    public override string Name => "ProximityGrabCustomGesture";
    public override string Author => "YourName";
    public override string Version => VERSION_CONSTANT;
    public override string Link => "https://github.com/<tbd>/ProximityGrabCustomGesture/";

    public override void OnEngineInit()
    {
        Msg("ProximityGrabCustomGesture loading...");
        var harmony = new Harmony("YourName.ProximityGrabCustomGesture");
        Methods.HarmonyInstance = harmony;
        harmony.PatchAll();
        Msg("ProximityGrabCustomGesture loaded. Fist = proximity-only grab while hands are tracked.");
    }
}