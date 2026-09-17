using HarmonyLib;
using ResoniteModLoader;

namespace ProximityGrabCustomGesture;

public class ProximityGrabCustomGestureMod : ResoniteMod
{
    internal const string VERSION_CONSTANT = "0.5.0";

    // RML configuration keys (persisted to rml_config/ProximityGrabCustomGesture.json,
    // editable via config-manager UIs or by editing the JSON while the game is stopped).
    // NOTE: keys must stay declared before the mirrored statics below: the statics
    // initialize from Key.Value, and C# initializes static fields in textual order.
    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> FistGrabEnabledKey =
        new("FistGrabEnabled", "Fist gesture triggers proximity grab", () => false);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> PrecisionGrabEnabledKey =
        new("PrecisionGrabEnabled", "Index-thumb pinch triggers precision grab", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> DebugShowPinchKey =
        new("DebugShowPinch", "World-space debug text above each wrist", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistEngageDegreesKey =
        new("FistEngageDegrees", "Average finger curl to engage fist grab (degrees)", () => 150f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistReleaseDegreesKey =
        new("FistReleaseDegrees", "Average finger curl to release fist grab (degrees)", () => 100f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistMinEngageDegreesKey =
        new("FistMinEngageDegrees", "Per-finger minimum curl to engage fist grab (degrees)", () => 90f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistMinReleaseDegreesKey =
        new("FistMinReleaseDegrees", "Per-finger minimum curl to hold fist grab (degrees)", () => 70f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PinchEngageDistanceKey =
        new("PinchEngageDistance", "Index-thumb tip distance to engage pinch (m)", () => 0.030f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PinchReleaseDistanceKey =
        new("PinchReleaseDistance", "Index-thumb tip distance to release pinch (m)", () => 0.050f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PinchMaxIndexCurlDegreesKey =
        new("PinchMaxIndexCurlDegrees", "Index curl above which pinch is rejected (degrees)", () => 140f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PrecisionMinRadiusKey =
        new("PrecisionMinRadius", "Precision grab sphere sweep start radius (m)", () => 0.004f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PrecisionMaxRadiusKey =
        new("PrecisionMaxRadius", "Precision grab sphere sweep max radius (m)", () => 0.04f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PrecisionRadiusStepKey =
        new("PrecisionRadiusStep", "Precision grab sphere sweep radius step (m)", () => 0.01f, valueValidator: IsNonNegativeFinite);

    internal static ModConfiguration? Config;

    // Gesture toggles (hot-path source of truth, mirrored from config).
    // Initialized from the keys so each default literal exists exactly once, above.
    internal static bool FistGrabEnabled = FistGrabEnabledKey.Value;
    internal static bool PrecisionGrabEnabled = PrecisionGrabEnabledKey.Value;

    // Debug overlay (world-space text above each wrist)
    internal static bool DebugShowPinch = DebugShowPinchKey.Value;

    // Fist gesture (average curl, degrees + per-finger minimum, degrees)
    internal static float FistEngageDegrees = FistEngageDegreesKey.Value;
    internal static float FistReleaseDegrees = FistReleaseDegreesKey.Value;
    internal static float FistMinEngageDegrees = FistMinEngageDegreesKey.Value;
    internal static float FistMinReleaseDegrees = FistMinReleaseDegreesKey.Value;

    // Pinch gesture (scale-normalized index <-> thumb tip distance, m)
    internal static float PinchEngageDistance = PinchEngageDistanceKey.Value;
    internal static float PinchReleaseDistance = PinchReleaseDistanceKey.Value;
    internal static float PinchMaxIndexCurlDegrees = PinchMaxIndexCurlDegreesKey.Value;

    // Precision grab sphere sweep
    internal static float PrecisionMinRadius = PrecisionMinRadiusKey.Value;
    internal static float PrecisionMaxRadius = PrecisionMaxRadiusKey.Value;
    internal static float PrecisionRadiusStep = PrecisionRadiusStepKey.Value;

    public override string Name => "ProximityGrabCustomGesture";
    public override string Author => "YourName";
    public override string Version => VERSION_CONSTANT;
    public override string Link => "https://github.com/<tbd>/ProximityGrabCustomGesture/";

    public override void OnEngineInit()
    {
        Msg("ProximityGrabCustomGesture loading...");
        Config = GetConfiguration();
        if (Config == null)
        {
            Warn("No ModConfiguration (no config keys registered); using built-in defaults.");
        }
        else
        {
            CopyFromConfig();
            Config.OnThisConfigurationChanged += _ => CopyFromConfig();
        }
        var harmony = new Harmony("YourName.ProximityGrabCustomGesture");
        Methods.HarmonyInstance = harmony;
        harmony.PatchAll();
        Msg("ProximityGrabCustomGesture loaded. Fist = proximity grab, pinch = precision grab (fist wins) while hands are tracked.");
    }

    private static bool IsNonNegativeFinite(float value) =>
        float.IsFinite(value) && value >= 0f;

    private static void CopyFromConfig()
    {
        FistGrabEnabled = FistGrabEnabledKey.Value;
        PrecisionGrabEnabled = PrecisionGrabEnabledKey.Value;
        DebugShowPinch = DebugShowPinchKey.Value;
        FistEngageDegrees = FistEngageDegreesKey.Value;
        FistReleaseDegrees = FistReleaseDegreesKey.Value;
        FistMinEngageDegrees = FistMinEngageDegreesKey.Value;
        FistMinReleaseDegrees = FistMinReleaseDegreesKey.Value;
        PinchEngageDistance = PinchEngageDistanceKey.Value;
        PinchReleaseDistance = PinchReleaseDistanceKey.Value;
        PinchMaxIndexCurlDegrees = PinchMaxIndexCurlDegreesKey.Value;
        PrecisionMinRadius = PrecisionMinRadiusKey.Value;
        PrecisionMaxRadius = PrecisionMaxRadiusKey.Value;
        PrecisionRadiusStep = Math.Max(PrecisionRadiusStepKey.Value, 0.0005f);
        if (PinchEngageDistance >= PinchReleaseDistance)
            PinchEngageDistance = PinchReleaseDistance * 0.5f;
        if (PrecisionMinRadius >= PrecisionMaxRadius)
            PrecisionMinRadius = PrecisionMaxRadius * 0.5f;
    }
}
