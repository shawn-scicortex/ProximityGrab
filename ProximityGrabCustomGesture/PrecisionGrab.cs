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

    internal static string LastAttemptDetail = "never";

    public static bool TryGrab(InteractionHandler handler, Hand? hand)
    {
        LastAttemptDetail = "running";
        if (hand == null)
        {
            LastAttemptDetail = "no-hand";
            return false;
        }
        var root = handler.LocalUserRoot;
        if (root == null)
        {
            LastAttemptDetail = "no-root";
            return false;
        }

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
            {
                LastAttemptDetail = "sweep=0";
                return false;
            }

            int resolved = CountResolvedGrabbables(handler, colliders);
            if (resolved == 0)
            {
                LastAttemptDetail = $"no-grabbable c={colliders.Count}";
                return false;
            }

            bool grabbed = handler.Grabber?.Grab(colliders, g => FilterGrabbable.Invoke(handler, new object[] { g }) is true, true, true) ?? false;
            if (!grabbed)
            {
                LastAttemptDetail = $"grab=false c={colliders.Count} g={resolved}";
                return false;
            }

            LastAttemptDetail = "ok";
            SetGrabMaterial(handler);
            return true;
        }
        catch (System.Exception)
        {
            LastAttemptDetail = "exception";
            throw;
        }
        finally
        {
            Pool.Return(ref colliders);
        }
    }

    private static int CountResolvedGrabbables(InteractionHandler handler, List<ICollider> colliders)
    {
        HashSet<IGrabbable> found = Pool.BorrowHashSet<IGrabbable>();
        try
        {
            foreach (var collider in colliders)
            {
                var slot = collider.Slot;
                while (slot != null)
                {
                    IGrabbable grabbable = slot.GetComponent<IGrabbable>();
                    if (grabbable != null && (bool)(FilterGrabbable.Invoke(handler, new object[] { grabbable }) ?? false))
                    {
                        found.Add(grabbable);
                        break;
                    }
                    slot = slot.Parent;
                }
            }
            return found.Count;
        }
        finally
        {
            Pool.Return(ref found);
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