using System;
using System.Collections.Generic;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;

namespace ProximityGrabCustomGesture;

internal static class PrecisionGrab
{
    private static readonly MethodInfo FilterGrabbable = AccessTools.Method(typeof(InteractionHandler), "FilterGrabbable")!;

    private static readonly FieldInfo GrabMaterial = AccessTools.Field(typeof(InteractionHandler), "_grabMaterial")!;

    public static bool TryGrab(InteractionHandler handler, Hand? hand)
    {
        if (hand == null)
            return false;
        var root = handler.LocalUserRoot;
        if (root == null)
            return false;

        var indexTip = root.Slot.LocalPointToGlobal(hand.Index.Tip.Position);
        var thumbTip = root.Slot.LocalPointToGlobal(hand.Thumb.Tip.Position);
        float scale = root.GlobalScale;
        float3 origin = MathX.Lerp(indexTip, thumbTip, 0.5f);

        List<ICollider> colliders = Pool.BorrowList<ICollider>();
        try
        {
            for (float radius = ProximityGrabCustomGestureMod.PrecisionMinRadius;
                 radius < ProximityGrabCustomGestureMod.PrecisionMaxRadius;
                 radius += ProximityGrabCustomGestureMod.PrecisionRadiusStep)
            {
                handler.World.Physics.SphereOverlap(in origin, scale * radius, colliders);
            }
            if (colliders.Count == 0)
                return false;

            bool grabbed = handler.Grabber?.Grab(colliders, g => FilterGrabbable.Invoke(handler, new object[] { g }) is true, true, true) ?? false;
            if (!grabbed)
                return false;

            SetGrabMaterial(handler);
            return true;
        }
        finally
        {
            Pool.Return(ref colliders);
        }
    }

    private static void SetGrabMaterial(InteractionHandler handler)
    {
        try
        {
            if (GrabMaterial.GetValue(handler) is not SyncRef<FresnelMaterial> material || material.Target == null)
                return;
            material.Target.NearColor.Value = RadiantUI_Constants.Dark.RED;
            material.Target.FarColor.Value = RadiantUI_Constants.Hero.RED.SetValue(0.5f);
        }
        catch (Exception e)
        {
            UniLog.Warning($"ProximityGrabCustomGesture: failed to tint precision grab material: {e}");
        }
    }
}