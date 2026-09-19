using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;

namespace ProximityGrab;

internal static class PrecisionGrab
{
    private static readonly MethodInfo FilterGrabbable = AccessTools.Method(typeof(InteractionHandler), "FilterGrabbable")!;

    private static readonly FieldInfo GrabMaterial = AccessTools.Field(typeof(InteractionHandler), "_grabMaterial")!;

    internal static string LastAttemptDetail = "never";

    private static bool _frameDiagLogged;

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

        Slot frame = root.Slot;
        float3 wristWorld = frame.LocalPointToGlobal(hand.Wrist.Position);
        floatQ wristRot = frame.LocalRotationToGlobal(hand.Wrist.Rotation);
        var indexTip = wristWorld + wristRot * hand.Index.Tip.Position;
        var thumbTip = wristWorld + wristRot * hand.Thumb.Tip.Position;
        float scale = 1f;
        float3 origin = MathX.Lerp(indexTip, thumbTip, 0.5f);

        void LogDiag()
        {
            // UniLog.Log("ProximityGrab: frame diag " + BuildFrameDiag(handler, hand, root, frame, indexTip, thumbTip, origin));
        }

        if (!_frameDiagLogged)
        {
            _frameDiagLogged = true;
            LogDiag();
        }

        List<ICollider> colliders = Pool.BorrowList<ICollider>();
        try
        {
            for (float radius = ProximityGrabMod.PrecisionMinRadius;
                 radius < ProximityGrabMod.PrecisionMaxRadius;
                 radius += ProximityGrabMod.PrecisionRadiusStep)
            {
                handler.World.Physics.SphereOverlap(in origin, scale * radius, colliders);
            }
            if (colliders.Count == 0)
            {
                LastAttemptDetail = $"sweep=0 s={scale:0.##}";
                LogDiag();
                return false;
            }

            var grabber = handler.Grabber;
            Predicate<IGrabbable> filter = g => (bool)(FilterGrabbable.Invoke(handler, new object[] { g }) ?? false);

            string firstRaw = "";
            int raw = FindGrabbables(grabber, colliders, g => true, ref firstRaw);
            if (raw == 0)
            {
                LastAttemptDetail = $"no-grabbable c={colliders.Count} {HitNames(colliders)}";
                LogDiag();
                return false;
            }
            string firstFiltered = "";
            int resolved = FindGrabbables(grabber, colliders, filter, ref firstFiltered);
            if (resolved == 0)
            {
                LastAttemptDetail = $"filter-rejected c={colliders.Count} r={raw} [{firstRaw}] {HitNames(colliders)}";
                LogDiag();
                return false;
            }

            bool grabbed = grabber?.Grab(colliders, filter, true, true) ?? false;
            if (!grabbed)
            {
                LastAttemptDetail = $"grab=false c={colliders.Count} g={resolved} [{firstFiltered}] {HitNames(colliders)}";
                LogDiag();
                return false;
            }

            LastAttemptDetail = "ok";
            SetGrabMaterial(handler);
            return true;
        }
        catch (System.Exception)
        {
            LastAttemptDetail = "exception";
            LogDiag();
            throw;
        }
        finally
        {
            Pool.Return(ref colliders);
        }
    }

    private static int FindGrabbables(Grabber? grabber, List<ICollider> colliders, Predicate<IGrabbable> filter, ref string firstName)
    {
        if (grabber == null)
            return 0;
        HashSet<IGrabbable> found = Pool.BorrowHashSet<IGrabbable>();
        try
        {
            foreach (var collider in colliders)
            {
                IGrabbable? grabbable = grabber.FindGrabbableInParents(collider.Slot, filter);
                if (grabbable != null && found.Add(grabbable))
                {
                    if (firstName.Length == 0)
                        firstName = GrabbableName(grabbable);
                }
            }
            return found.Count;
        }
        finally
        {
            Pool.Return(ref found);
        }
    }

    private static string GrabbableName(IGrabbable grabbable)
    {
        Slot slot = grabbable.Slot;
        if (slot == null)
            return "?";
        return slot.GetObjectRoot()?.Name ?? slot.Name;
    }

    private static string HitNames(List<ICollider> colliders)
    {
        HashSet<string> names = new();
        StringBuilder sb = new StringBuilder();
        sb.Append("hits:[");
        foreach (var collider in colliders)
        {
            string name = collider.Slot?.Name ?? "?";
            if (!names.Add(name))
                continue;
            if (names.Count > 1)
                sb.Append(',');
            sb.Append(name);
            if (names.Count >= 6)
                break;
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string BuildFrameDiag(InteractionHandler handler, Hand hand, UserRoot root, Slot frame, in float3 indexTip, in float3 thumbTip, in float3 origin)
    {
        Slot rootSlot = root.Slot;
        float3 indexRaw = hand.Index.Tip.Position;
        float3 thumbRaw = hand.Thumb.Tip.Position;
        float3 wristRaw = hand.Wrist.Position;
        float3 rootRot = rootSlot.GlobalRotation.EulerAngles;
        float3 wristRot = hand.Wrist.Rotation.EulerAngles;
        float3 wristWorld = rootSlot.LocalPointToGlobal(wristRaw);
        float3 wristRotWorld = rootSlot.LocalRotationToGlobal(hand.Wrist.Rotation).EulerAngles;
        return $"frame:{frame.Name} rootPos:{rootSlot.GlobalPosition:F3} rot:{rootRot:F1} s:{root.GlobalScale:F3} "
            + $"wrist:{wristRaw:F3} world:{wristWorld:F3} rot:{wristRot:F1} rotWorld:{wristRotWorld:F1} "
            + $"idxRaw:{indexRaw:F3}->{indexTip:F3} thbRaw:{thumbRaw:F3}->{thumbTip:F3} origin:{origin:F3}";
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
            UniLog.Warning($"ProximityGrab: failed to tint precision grab material: {e}");
        }
    }
}
