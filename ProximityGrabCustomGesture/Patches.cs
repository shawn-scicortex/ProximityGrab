using System;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;

namespace ProximityGrabCustomGesture;

internal static class Methods
{
    private static readonly MethodInfo[] GrabMethods = typeof(InteractionHandler)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .Where(m => m.Name == "Grab")
        .ToArray();

    internal static readonly MethodInfo StartGrab = AccessTools.Method(typeof(InteractionHandler), "StartGrab")!;
    internal static readonly MethodInfo HoldGrab = AccessTools.Method(typeof(InteractionHandler), "HoldGrab")!;
    internal static readonly MethodInfo EndGrab = AccessTools.Method(typeof(InteractionHandler), "EndGrab")!;
    internal static readonly MethodInfo GrabNoLaser = GrabMethods.First(m => m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(bool));
    internal static readonly FieldInfo GrabBlockActions = AccessTools.Field(typeof(InteractionHandler), "_grabBlockActions")!;
}

// Runs the fist-gesture detector every frame for the local user.
[HarmonyPatch(typeof(InteractionHandler))]
[HarmonyPatch("OnCommonUpdate")]
internal static class Patch_InteractionHandler_OnCommonUpdate
{
    [HarmonyPostfix]
    private static void Postfix(InteractionHandler __instance)
    {
        try
        {
            if (!__instance.IsOwnedByLocalUser)
                return;
            if (__instance.World.Focus == World.WorldFocus.Background)
                return;
            FistGesture.Update(__instance);
        }
        catch (System.Exception e)
        {
            UniLog.Error($"ProximityGrabCustomGesture: failed to update gesture for {__instance}: {e}");
        }
    }
}

// While hands are tracked: no fist -> suppress grab entirely; fist -> proximity grab only (never laser).
// No hands tracked: stock behavior.
[HarmonyPatch(typeof(InteractionHandler))]
[HarmonyPatch("Grab")]
[HarmonyPatch(new System.Type[0])]
internal static class Patch_InteractionHandler_Grab
{
    [HarmonyPrefix]
    private static bool Prefix(InteractionHandler __instance, ref bool __result)
    {
        var state = ProximityGrabState.Get(__instance);
        if (!state.HasTrackingHands)
            return true;
        if (!state.ProximityGrabActive)
        {
            __result = false;
            return false;
        }
        __result = (bool)(Methods.GrabNoLaser.Invoke(__instance, new object[] { false, null! }) ?? false);
        return false;
    }
}

// While hands are tracked, remove the local user's built-in Grab binding so the grab command
// itself can never trigger a grab; the fist detector is the only grab source.
[HarmonyPatch(typeof(InputInterface))]
[HarmonyPatch("Bind")]
[HarmonyPatch(new System.Type[] { typeof(InputGroup) })]
internal static class Patch_InputInterface_Bind
{
    [HarmonyPostfix]
    private static void Postfix(InputGroup group)
    {
        try
        {
            if (!FistGesture.HasTrackingHands)
                return;
            if (group is not InteractionHandlerInputs handlerInputs)
                return;
            if (group.Owner is not InteractionHandler handler || !handler.IsOwnedByLocalUser)
                return;
            handlerInputs.Grab.ClearBindings();
        }
        catch (System.Exception e)
        {
            UniLog.Error($"ProximityGrabCustomGesture: failed to strip grab bindings: {e}");
        }
    }
}