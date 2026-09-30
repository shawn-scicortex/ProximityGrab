/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using Renderite.Shared;

namespace ProximityGrab;

internal static class PrecisionGrab
{
    private static readonly MethodInfo FilterGrabbable = AccessTools.Method(typeof(InteractionHandler), "FilterGrabbable")!;

    private static readonly FieldInfo GrabMaterial = AccessTools.Field(typeof(InteractionHandler), "_grabMaterial")!;

    internal static string LastAttemptDetail = "never";

    private static bool _frameDiagLogged;

    public static bool TryGrab(InteractionHandler handler, Hand? hand, FingerType fingerType)
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
        // Sweep radii are in user space; scale them with the user like the
        // engine does for GRAB_RADIUS.
        float scale = root.GlobalScale;
        if (!TryGetPinchPoints(handler, hand, fingerType, out float3 fingerTip, out float3 thumbTip))
        {
            LastAttemptDetail = "no-tips";
            return false;
        }
        float3 origin = MathX.Lerp(fingerTip, thumbTip, 0.5f);

        void LogDiag()
        {
            // UniLog.Log("ProximityGrab: frame diag " + BuildFrameDiag(handler, hand, root, frame, fingerTip, thumbTip, origin));
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
                FlashMiss(handler, origin, scale, fingerType);
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
                FlashMiss(handler, origin, scale, fingerType);
                return false;
            }
            string firstFiltered = "";
            int resolved = FindGrabbables(grabber, colliders, filter, ref firstFiltered);
            if (resolved == 0)
            {
                LastAttemptDetail = $"filter-rejected c={colliders.Count} r={raw} [{firstRaw}] {HitNames(colliders)}";
                LogDiag();
                FlashMiss(handler, origin, scale, fingerType);
                return false;
            }

            bool grabbed = grabber?.Grab(colliders, filter, true, true) ?? false;
            if (!grabbed)
            {
                LastAttemptDetail = $"grab=false c={colliders.Count} g={resolved} [{firstFiltered}] {HitNames(colliders)}";
                LogDiag();
                FlashMiss(handler, origin, scale, fingerType);
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

    // Missed-pinch feedback: the pinch sweep is invisible without debug
    // visuals, so show a brief effect at the sweep center when a pinch ran the
    // sweep but grabbed nothing (see MissEffect). Skipped in Userspace, whose
    // handlers also run every pinch and miss whenever the grab lands in the
    // world, which would fire on every successful world grab.
    private static void FlashMiss(InteractionHandler handler, in float3 origin, float scale, FingerType fingerType)
    {
        if (ProximityGrabMod.PinchMissEffect == PinchMissEffectKind.Off || ProximityGrabMod.PinchMissFlashSeconds <= 0f)
            return;
        if (handler.World == Userspace.UserspaceWorld)
            return;
        MissEffect.Trigger(handler, ProximityGrabState.Get(handler), origin, scale, fingerType);
    }

    private sealed class RigHolder
    {
        public BipedRig? Rig;
        public bool Checked;
    }

    private static readonly ConditionalWeakTable<HandPoser, RigHolder> Rigs = new();

    // Pinch point pair for one finger + thumb, in global space. Prefers the
    // avatar's fingers so the sweep stays on the visible hand even when the
    // tracked hand reaches past the avatar's arm: rigged tip bones when the
    // avatar has them, else the HandPoser distal bones (what the engine's own
    // PrecisionGrab uses). Falls back to the tracking skeleton only when the
    // avatar can't supply both points, so the pair is never mixed.
    internal static bool TryGetPinchPoints(InteractionHandler handler, Hand hand, FingerType fingerType, out float3 fingerPoint, out float3 thumbPoint)
    {
        fingerPoint = float3.Zero;
        thumbPoint = float3.Zero;
        var root = handler.LocalUserRoot;
        if (root == null)
            return false;

        Chirality side = handler.Side.Value;
        HandPoser? poser = root.GetRegisteredComponent((HandPoser p) => p.Side.Value == side);
        if (poser != null && !poser.IsRemoved)
        {
            BipedRig? rig = GetRig(poser);
            Slot? fingerSlot = AvatarPoint(poser, rig, fingerType, side);
            Slot? thumbSlot = AvatarPoint(poser, rig, FingerType.Thumb, side);
            if (fingerSlot != null && thumbSlot != null)
            {
                fingerPoint = fingerSlot.GlobalPosition;
                thumbPoint = thumbSlot.GlobalPosition;
                return true;
            }
        }

        Finger finger = hand[fingerType];
        if (!finger.Tip.IsTracking || !hand.Thumb.Tip.IsTracking)
            return false;
        Slot frame = root.Slot;
        float scale = root.GlobalScale;
        float3 wristWorld = frame.LocalPointToGlobal(hand.Wrist.Position);
        floatQ wristRot = frame.LocalRotationToGlobal(hand.Wrist.Rotation);
        fingerPoint = wristWorld + wristRot * (finger.Tip.Position * scale);
        thumbPoint = wristWorld + wristRot * (hand.Thumb.Tip.Position * scale);
        return true;
    }

    private static Slot? AvatarPoint(HandPoser poser, BipedRig? rig, FingerType fingerType, Chirality side)
    {
        Slot? slot = rig?.TryGetBone(fingerType.ComposeFinger(FingerSegmentType.Tip, side));
        if (slot == null || slot.IsRemoved)
            slot = poser[fingerType]?.FarthestSegment?.Root.Target;
        return slot == null || slot.IsRemoved ? null : slot;
    }

    // FindCompatibleRig walks the hierarchy, so cache it per poser; a new
    // avatar brings a new HandPoser, which invalidates this naturally.
    private static BipedRig? GetRig(HandPoser poser)
    {
        RigHolder holder = Rigs.GetValue(poser, _ => new RigHolder());
        // Rigless avatars are looked up once, not every frame.
        if (!holder.Checked || (holder.Rig != null && holder.Rig.IsRemoved))
        {
            holder.Rig = poser.FindCompatibleRig();
            holder.Checked = true;
        }
        return holder.Rig;
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
