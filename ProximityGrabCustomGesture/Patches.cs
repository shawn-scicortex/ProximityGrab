using System;
using System.Collections.Generic;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;

namespace ProximityGrabCustomGesture;

internal static class Methods
{
    internal static Harmony? HarmonyInstance;

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

// No hands tracked: stock behavior passed straight through.
// Fist gesture: proximity grab only (never laser).
// Pinch gesture: precision grab at the tracked index/thumb pinch point.
// Other grab commands while hands tracked (gamepad/keyboard keep their bindings): stock path.
[HarmonyPatch(typeof(InteractionHandler))]
[HarmonyPatch("Grab")]
[HarmonyPatch(new System.Type[0])]
internal static class Patch_InteractionHandler_Grab
{
    [HarmonyPrefix]
    private static bool Prefix(InteractionHandler __instance, ref bool __result)
    {
        var state = ProximityGrabState.Get(__instance);
        if (!state.GestureDriven)
            return true;
        state.GestureDriven = false;
        try
        {
            __result = state.ActiveGesture == GrabGestureKind.Pinch
                ? PrecisionGrab.TryGrab(__instance, state.GestureHand)
                : (bool)(Methods.GrabNoLaser.Invoke(__instance, new object[] { false, null! }) ?? false);
        }
        catch (System.Exception e)
        {
            UniLog.Error($"ProximityGrabCustomGesture: gesture grab failed: {e}");
            __result = false;
        }
        state.LastGrabResult = (bool)__result;
        if (!state.LastGrabResult
            && state.ActiveGesture == GrabGestureKind.Pinch
            && ProximityGrabCustomGestureMod.DebugShowPinch)
        {
            UniLog.Log($"ProximityGrabCustomGesture: pinch grab failed: {PrecisionGrab.LastAttemptDetail}");
        }
        return false;
    }
}

// While hands are tracked, unbind only VR-controller grab sources so the grab command can never fire
// from a controller/skeleton false positive; gamepad and keyboard/mouse grab bindings are kept.
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
            RemoveControllerBindings(handlerInputs.Grab);
        }
        catch (System.Exception e)
        {
            UniLog.Error($"ProximityGrabCustomGesture: failed to strip grab bindings: {e}");
        }
    }

    internal static void RemoveControllerBindings(DigitalAction grab)
    {
        var bindingsField = typeof(InputAction<bool>).GetField("_bindings", BindingFlags.NonPublic | BindingFlags.Instance);
        if (bindingsField?.GetValue(grab) is not List<InputBinding<bool>> bindings)
            return;
        bindings.RemoveAll(b => b.ImplicitDevice is ControllerBase);
    }
}