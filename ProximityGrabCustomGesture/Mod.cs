using HarmonyLib;
using ResoniteModLoader;

namespace ProximityGrabCustomGesture;

public class ProximityGrabCustomGestureMod : ResoniteMod
{
    internal const string VERSION_CONSTANT = "0.3.11";

    // Gesture toggles
    internal static bool FistGrabEnabled = false;
    internal static bool PrecisionGrabEnabled = true;

    // Debug overlay (world-space text above each wrist)
    internal static bool DebugShowPinch = true;

    // Fist gesture (average curl, degrees + per-finger minimum, degrees)
    internal static float FistEngageDegrees = 150f;
    internal static float FistReleaseDegrees = 100f;
    internal static float FistMinEngageDegrees = 90f;
    internal static float FistMinReleaseDegrees = 70f;

    // Pinch gesture (scale-normalized index <-> thumb tip distance, m)
    internal static float PinchEngageDistance = 0.030f;
    internal static float PinchReleaseDistance = 0.050f;
    internal static float PinchMaxIndexCurlDegrees = 140f;

    // Precision grab sphere sweep
    internal static float PrecisionMinRadius = 0.005f;
    internal static float PrecisionMaxRadius = 0.05f;
    internal static float PrecisionRadiusStep = 0.01f;

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
        Msg("ProximityGrabCustomGesture loaded. Fist = proximity grab, pinch = precision grab (fist wins) while hands are tracked.");
    }
}
