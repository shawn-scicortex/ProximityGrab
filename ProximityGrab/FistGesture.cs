using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

namespace ProximityGrab;

internal static class FistGesture
{
    private static readonly List<Hand> Devices = new();

    private static bool LastGestureMode = true;

    public static bool HasTrackingHands { get; private set; }

    public static void Update(InteractionHandler handler)
    {
        var state = ProximityGrabState.Get(handler);

        Devices.Clear();
        handler.World.InputInterface.GetDevices(Devices);

        Hand? left = null;
        Hand? right = null;
        bool anyHands = false;
        foreach (var hand in Devices)
        {
            if (!hand.IsTracking)
                continue;
            anyHands = true;
            if (hand.Chirality == Chirality.Left)
            {
                if (left == null || hand.Wrist.Priority > left.Wrist.Priority)
                    left = hand;
            }
            else if (right == null || hand.Wrist.Priority > right.Wrist.Priority)
            {
                right = hand;
            }
        }

        if (state.HasTrackingHands != anyHands)
        {
            state.HasTrackingHands = anyHands;
            handler.Input.InvalidateBindings();
        }

        Hand? ownHand = handler.Side.Value == Chirality.Left ? left : right;
        // Mirror the RML GestureMode key into this hand's menu field so config-UI
        // edits appear within a frame, and rebuild bindings on mode transitions so
        // the controller grip bindings follow immediately from any source.
        ValueField<bool> gestureField = MenuPatches.GetGestureField(handler);
        if (gestureField.Value.Value != ProximityGrabMod.GestureMode)
            gestureField.Value.Value = ProximityGrabMod.GestureMode;
        if (ProximityGrabMod.GestureMode != LastGestureMode)
        {
            LastGestureMode = ProximityGrabMod.GestureMode;
            handler.Input.InvalidateBindings();
        }
        if (!ProximityGrabMod.GestureMode)
        {
            // Gesture mode off: fully-stock behavior. Clear hysteresis memories so
            // re-enabling starts clean; any in-flight gesture grab ends via Release below.
            state.FistEngaged = false;
            state.PinchEngagedIndex = false;
            state.PinchEngagedMiddle = false;
            state.PinchEngagedRing = false;
            state.GestureDriven = false;
        }
        bool fistRaw = ProximityGrabMod.GestureMode && UpdateFist(ownHand, ref state.FistEngaged);
        bool pinchRaw = ProximityGrabMod.GestureMode && UpdatePinchFingers(handler, ownHand, state);
        bool fist = ProximityGrabMod.FistGrabEnabled && fistRaw;
        bool pinch = ProximityGrabMod.PrecisionGrabEnabled && pinchRaw && !fist;

        if (!state.HasTrackingHands)
        {
            fist = false;
            pinch = false;
            state.GestureDriven = false;
            state.GestureHand = null;
        }

        GrabGestureKind gesture = fist
            ? GrabGestureKind.Fist
            : pinch ? GrabGestureKind.Pinch : GrabGestureKind.None;

        DrawDebug(handler, ownHand, state);

        if (state.ProximityGrabActive && gesture == GrabGestureKind.None)
        {
            Release(handler, state);
        }
        else if (!state.ProximityGrabActive && gesture != GrabGestureKind.None)
        {
            // Occupied hand (holding or grip-equipped, mirroring the engine's own
            // IsHoldingObjects || HasGripEquippedTool test): gestures wait.
            // A grip-equipped tool leaves the Grabber empty, so IsHoldingObjects
            // alone would miss it and EndGrab would later drop both items.
            if (handler.Grabber?.IsHoldingObjects == true || handler.HasGripEquippedTool)
                return;
            state.ProximityGrabActive = true;
            state.ActiveGesture = gesture;
            state.GestureHand = ownHand;
            state.GestureDriven = true;
            Methods.StartGrab.Invoke(handler, null);
        }
        else if (state.ProximityGrabActive)
        {
            Methods.HoldGrab.Invoke(handler, null);
        }
    }

    private static bool UpdateFist(Hand? hand, ref bool prevEngaged)
    {
        if (!IsUsable(hand))
        {
            prevEngaged = false;
            return false;
        }
        float avg = AverageCurlDegrees(hand!);
        float min = MinCurlDegrees(hand!);
        float minJoint = MinJointCurlDegrees(hand!);
        bool engaged = prevEngaged
            ? avg > ProximityGrabMod.FistReleaseDegrees
              && min > ProximityGrabMod.FistMinReleaseDegrees
              && minJoint > ProximityGrabMod.FistMinJointReleaseDegrees
            : avg > ProximityGrabMod.FistEngageDegrees
              && min > ProximityGrabMod.FistMinEngageDegrees
              && minJoint > ProximityGrabMod.FistMinJointEngageDegrees;
        prevEngaged = engaged;
        return engaged;
    }

    // Nearest engaged fingertip wins; each finger keeps its own hysteresis
    // memory so engage/release thresholds don't leak across fingers.
    private static bool UpdatePinchFingers(InteractionHandler handler, Hand? hand, ProximityGrabState state)
    {
        bool pinchIndex = false, pinchMiddle = false, pinchRing = false;
        float dIndex = float.NaN, dMiddle = float.NaN, dRing = float.NaN;
        if (ProximityGrabMod.IndexPinchEnabled)
            pinchIndex = UpdatePinchFinger(handler, hand, FingerType.Index, ref state.PinchEngagedIndex, out dIndex);
        else
            state.PinchEngagedIndex = false;
        if (ProximityGrabMod.MiddlePinchEnabled)
            pinchMiddle = UpdatePinchFinger(handler, hand, FingerType.Middle, ref state.PinchEngagedMiddle, out dMiddle);
        else
            state.PinchEngagedMiddle = false;
        if (ProximityGrabMod.RingPinchEnabled)
            pinchRing = UpdatePinchFinger(handler, hand, FingerType.Ring, ref state.PinchEngagedRing, out dRing);
        else
            state.PinchEngagedRing = false;

        bool pinchRaw = false;
        float best = float.PositiveInfinity;
        FingerType winner = state.GestureFinger;
        if (pinchIndex && dIndex < best) { pinchRaw = true; best = dIndex; winner = FingerType.Index; }
        if (pinchMiddle && dMiddle < best) { pinchRaw = true; best = dMiddle; winner = FingerType.Middle; }
        if (pinchRing && dRing < best) { pinchRaw = true; best = dRing; winner = FingerType.Ring; }
        if (pinchRaw)
            state.GestureFinger = winner;
        return pinchRaw;
    }

    private static bool UpdatePinchFinger(InteractionHandler handler, Hand? hand, FingerType fingerType, ref bool prevEngaged, out float distance)
    {
        distance = float.NaN;
        if (hand == null)
        {
            prevEngaged = false;
            return false;
        }
        Finger finger = hand[fingerType];
        if (!IsUsableThumb(hand.Thumb)
            || !IsUsableFinger(finger)
            || !finger.Tip.IsTracking
            || !hand.Thumb.Tip.IsTracking)
        {
            prevEngaged = false;
            return false;
        }
        var root = handler.LocalUserRoot;
        if (root == null)
        {
            prevEngaged = false;
            return false;
        }
        if (CurlDegrees(finger) >= ProximityGrabMod.PinchMaxIndexCurlDegrees)
        {
            prevEngaged = false;
            return false;
        }
        float3 fingerTip = root.Slot.LocalPointToGlobal(finger.Tip.Position);
        float3 thumbTip = root.Slot.LocalPointToGlobal(hand.Thumb.Tip.Position);
        distance = (fingerTip - thumbTip).Magnitude / root.GlobalScale;
        bool engaged = prevEngaged
            ? distance < ProximityGrabMod.PinchReleaseDistance
            : distance < ProximityGrabMod.PinchEngageDistance;
        prevEngaged = engaged;
        return engaged;
    }

    private static bool IsUsable(Hand? hand)
    {
        return hand != null
               && IsUsableFinger(hand.Index)
               && IsUsableFinger(hand.Middle)
               && IsUsableFinger(hand.Ring)
               && IsUsableFinger(hand.Pinky);
    }

    private static bool IsUsableThumb(Finger thumb)
    {
        int count = 0;
        if (thumb.Metacarpal.IsTracking) count++;
        if (thumb.Proximal.IsTracking) count++;
        if (thumb.Distal.IsTracking) count++;
        if (thumb.Tip.IsTracking) count++;
        return count >= 3;
    }

    private static bool IsUsableFinger(Finger finger)
    {
        int count = 0;
        if (finger.Metacarpal.IsTracking) count++;
        if (finger.Proximal.IsTracking) count++;
        if (finger.Intermediate != null && finger.Intermediate.IsTracking) count++;
        if (finger.Distal.IsTracking) count++;
        if (finger.Tip.IsTracking) count++;
        return count >= 3;
    }

    private static float AverageCurlDegrees(Hand hand)
    {
        return (CurlDegrees(hand.Index)
                + CurlDegrees(hand.Middle)
                + CurlDegrees(hand.Ring)
                + CurlDegrees(hand.Pinky)) / 4f;
    }

    private static float MinCurlDegrees(Hand hand)
    {
        return MathX.Min(CurlDegrees(hand.Index), CurlDegrees(hand.Middle), CurlDegrees(hand.Ring), CurlDegrees(hand.Pinky));
    }

    // Smallest valid per-joint curl across the four fingers; untracked joints are skipped.
    // NaN (fail-safe: blocks the gate) only if no finger yields a valid joint.
    private static float MinJointCurlDegrees(Hand hand)
    {
        Span<float> joints = stackalloc float[3];
        float min = float.PositiveInfinity;
        int valid = 0;
        AccumulateMinJoint(hand.Index, joints, ref min, ref valid);
        AccumulateMinJoint(hand.Middle, joints, ref min, ref valid);
        AccumulateMinJoint(hand.Ring, joints, ref min, ref valid);
        AccumulateMinJoint(hand.Pinky, joints, ref min, ref valid);
        return valid > 0 ? min : float.NaN;
    }

    private static void AccumulateMinJoint(Finger finger, Span<float> joints, ref float min, ref int valid)
    {
        int count = JointCurlDegrees(finger, joints);
        for (int i = 0; i < count - 2 && i < joints.Length; i++)
        {
            if (float.IsNaN(joints[i]))
                continue;
            if (joints[i] < min)
                min = joints[i];
            valid++;
        }
    }

    private static float CurlDegrees(Finger finger)
    {
        Span<float> joints = stackalloc float[3];
        int count = JointCurlDegrees(finger, joints);
        if (count < 3)
            return float.NaN;

        float sum = 0f;
        for (int i = 0; i < count - 2; i++)
            sum += joints[i];
        return sum;
    }

    // Per-joint curl angles (MCP/PIP/DIP when fully tracked); returns tracked segment count.
    // Joints beyond count - 2 are untouched; count < 3 means no valid angles.
    private static int JointCurlDegrees(Finger finger, Span<float> joints)
    {
        Span<float3> positions = stackalloc float3[5];
        int count = 0;
        if (finger.Metacarpal.IsTracking) positions[count++] = finger.Metacarpal.Position;
        if (finger.Proximal.IsTracking) positions[count++] = finger.Proximal.Position;
        if (finger.Intermediate != null && finger.Intermediate.IsTracking) positions[count++] = finger.Intermediate.Position;
        if (finger.Distal.IsTracking) positions[count++] = finger.Distal.Position;
        if (finger.Tip.IsTracking) positions[count++] = finger.Tip.Position;
        if (count < 3)
            return count;

        float3 previous = (positions[1] - positions[0]).Normalized;
        int j = 0;
        for (int i = 1; i < count - 1; i++)
        {
            float3 current = (positions[i + 1] - positions[i]).Normalized;
            if (j < joints.Length)
                joints[j++] = MathX.Angle(in previous, in current);
            previous = current;
        }
        return count;
    }

    private static void DrawDebug(InteractionHandler handler, Hand? hand, ProximityGrabState state)
    {
        try
        {
            if (!ProximityGrabMod.DebugShowPinch || !ProximityGrabMod.GestureMode || hand == null)
                return;
            var root = handler.LocalUserRoot;
            if (root == null)
                return;
            string side = handler.Side.Value == Chirality.Left ? "L" : "R";
            string p = PrecisionGrab.LastAttemptDetail;
            if (p.Length > 36)
                p = p.Substring(0, 36);
            float fistAvg = AverageCurlDegrees(hand);
            float fistMin = MinCurlDegrees(hand);
            float fistMinJoint = MinJointCurlDegrees(hand);
            float fistThreshold = state.FistEngaged
                ? ProximityGrabMod.FistReleaseDegrees
                : ProximityGrabMod.FistEngageDegrees;
            string fistLine = $"{side} Fist avg:{FormatDegrees(fistAvg)}/{fistThreshold:0} min:{FormatDegrees(fistMin)} jm:{FormatDegrees(fistMinJoint)} eng:{(state.FistEngaged ? "Y" : "N")} eq:{(handler.HasGripEquippedTool ? "Y" : "N")}";
            Finger pinchFinger = hand[state.GestureFinger];
            float pinchDist = float.NaN;
            if (pinchFinger.Tip.IsTracking && hand.Thumb.Tip.IsTracking)
            {
                float3 pinchTip = root.Slot.LocalPointToGlobal(pinchFinger.Tip.Position);
                float3 pinchThumbTip = root.Slot.LocalPointToGlobal(hand.Thumb.Tip.Position);
                pinchDist = (pinchTip - pinchThumbTip).Magnitude / root.GlobalScale;
            }
            bool pinchAny = state.PinchEngagedIndex || state.PinchEngagedMiddle || state.PinchEngagedRing;
            string pinchLine = $"{side} Pinch {PinchFingerLabel(state.GestureFinger)} d:{pinchDist:F3} curl:{CurlDegrees(pinchFinger):F0}/{ProximityGrabMod.PinchMaxIndexCurlDegrees:0} th:{ThumbTrackingCount(hand)} eng:{(pinchAny ? "Y" : "N")}";
            Span<float> indexJoints = stackalloc float[3];
            int indexTracked = JointCurlDegrees(hand.Index, indexJoints);
            string indexLine = $"{side} Idx j:{FormatJoint(indexJoints, indexTracked, 0)}/{FormatJoint(indexJoints, indexTracked, 1)}/{FormatJoint(indexJoints, indexTracked, 2)} tot:{FormatDegrees(CurlDegrees(hand.Index))} n:{indexTracked}";
            Slot frame = root.Slot;
            float3 wristWorld = frame.LocalPointToGlobal(hand.Wrist.Position);
            floatQ wristRot = frame.LocalRotationToGlobal(hand.Wrist.Rotation);
            float3 wristMarker = wristWorld;
            float3 debugAnchorLine = wristMarker + wristRot * float3.Up * 0.12f;
            float3 debugAnchorLine2 = debugAnchorLine + wristRot * float3.Up * 0.11f;
            float3 debugAnchorLine3 = debugAnchorLine2 + wristRot * float3.Up * 0.11f;
            var fistColor = state.FistEngaged ? colorX.Green : colorX.White;
            var pinchColor = pinchAny ? colorX.Green : colorX.White;
            var indexColor = colorX.White;
            handler.Debug.Text(in debugAnchorLine, fistLine, 0.08f, in fistColor, 0f, true);
            handler.Debug.Text(in debugAnchorLine2, pinchLine, 0.08f, in pinchColor, 0f, true);
            handler.Debug.Text(in debugAnchorLine3, indexLine, 0.08f, in indexColor, 0f, true);
            if (pinchFinger.Tip.IsTracking && hand.Thumb.Tip.IsTracking)
            {
                float3 pinchMarker = wristWorld + wristRot * pinchFinger.Tip.Position;
                float3 thumbMarker = wristWorld + wristRot * hand.Thumb.Tip.Position;
                float3 originMarker = MathX.Lerp(pinchMarker, thumbMarker, 0.5f);
                float size = 0.06f;
                colorX cPinch = PinchFingerColor(state.GestureFinger);
                colorX cThumb = colorX.Green;
                colorX cOrigin = colorX.Yellow;
                colorX cWrist = colorX.Blue;
                handler.Debug.Text(in pinchMarker, PinchFingerLabel(state.GestureFinger), size, in cPinch, 0f, true);
                handler.Debug.Text(in thumbMarker, "T", size, in cThumb, 0f, true);
                handler.Debug.Text(in originMarker, "O", size, in cOrigin, 0f, true);
                handler.Debug.Text(in wristMarker, "W", size, in cWrist, 0f, true);
                DrawPinchFingerSpheres(handler, hand, wristWorld, wristRot, thumbMarker, state);
            }
            // Fist grab sphere: mirrors the engine non-laser overlap test
            // (InteractionHandler GRAB_RADIUS at Grabber slot, scaled by user root).
            var grabber = handler.Grabber;
            if (grabber != null)
            {
                float3 grabCenter = grabber.Slot.GlobalPosition;
                float grabRadius = InteractionHandler.GRAB_RADIUS * (handler.LocalUserRoot?.GlobalScale ?? 1f);
                colorX fistSphere = colorX.Cyan.SetA(0.08f);
                handler.Debug.Sphere(in grabCenter, grabRadius, in fistSphere, local: true);
            }
        }
        catch (Exception e)
        {
            UniLog.Error("ProximityGrab debug overlay failed: " + e);
        }
    }

    private static string FormatDegrees(float degrees) =>
        float.IsNaN(degrees) ? "---" : $"{degrees:F0}";

    private static colorX PinchFingerColor(FingerType finger) => finger switch
    {
        FingerType.Middle => colorX.Magenta,
        FingerType.Ring => colorX.Orange,
        _ => colorX.Red,
    };

    private static string PinchFingerLabel(FingerType finger) => finger switch
    {
        FingerType.Middle => "M",
        FingerType.Ring => "R",
        _ => "I",
    };

    // One grab sphere per enabled pinching finger: ghosts at very low alpha so
    // the user sees where each finger would sweep, with the actively pinching
    // finger popped to full debug alpha.
    private static void DrawPinchFingerSpheres(InteractionHandler handler, Hand hand, float3 wristWorld, floatQ wristRot, float3 thumbMarker, ProximityGrabState state)
    {
        DrawPinchFingerSphere(handler, hand, FingerType.Index, ProximityGrabMod.IndexPinchEnabled,
            state.GestureFinger == FingerType.Index && state.PinchEngagedIndex, wristWorld, wristRot, thumbMarker);
        DrawPinchFingerSphere(handler, hand, FingerType.Middle, ProximityGrabMod.MiddlePinchEnabled,
            state.GestureFinger == FingerType.Middle && state.PinchEngagedMiddle, wristWorld, wristRot, thumbMarker);
        DrawPinchFingerSphere(handler, hand, FingerType.Ring, ProximityGrabMod.RingPinchEnabled,
            state.GestureFinger == FingerType.Ring && state.PinchEngagedRing, wristWorld, wristRot, thumbMarker);
    }

    private static void DrawPinchFingerSphere(InteractionHandler handler, Hand hand, FingerType fingerType, bool enabled, bool active, float3 wristWorld, floatQ wristRot, float3 thumbMarker)
    {
        if (!enabled)
            return;
        Finger finger = hand[fingerType];
        if (!finger.Tip.IsTracking)
            return;
        float3 fingerMarker = wristWorld + wristRot * finger.Tip.Position;
        float3 origin = MathX.Lerp(fingerMarker, thumbMarker, 0.5f);
        colorX sphere = PinchFingerColor(fingerType).SetA(active ? 0.1f : 0.02f);
        handler.Debug.Sphere(in origin, ProximityGrabMod.PrecisionMaxRadius, in sphere, local: true);
    }

    private static string FormatJoint(Span<float> joints, int trackedCount, int joint) =>
        joint < trackedCount - 2 ? $"{joints[joint]:F0}" : "-";

    private static int ThumbTrackingCount(Hand hand)
    {
        int count = 0;
        if (hand.Thumb.Metacarpal.IsTracking) count++;
        if (hand.Thumb.Proximal.IsTracking) count++;
        if (hand.Thumb.Distal.IsTracking) count++;
        if (hand.Thumb.Tip.IsTracking) count++;
        return count;
    }

    private static void Release(InteractionHandler handler, ProximityGrabState state)
    {
        state.ProximityGrabActive = false;
        state.FistEngaged = false;
        state.PinchEngagedIndex = false;
        state.PinchEngagedMiddle = false;
        state.PinchEngagedRing = false;
        state.GestureHand = null;
        state.ActiveGesture = GrabGestureKind.None;
        Methods.EndGrab.Invoke(handler, new object[] { false });
        Methods.GrabBlockActions.SetValue(handler, false);
    }
}
