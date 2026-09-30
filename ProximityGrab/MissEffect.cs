/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using System;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using PD = FrooxEngine.PhotonDust;

namespace ProximityGrab;

// RML saves enums by name and resets the whole config file if a saved name no
// longer parses, so never rename or remove a member once it has shipped.
internal enum PinchMissEffectKind
{
    Random,
    Sparks,
    Ring,
    Off,
}

// Missed-pinch feedback: "a pinch fired, it grabbed nothing, here is where it
// registered". Every effect lives on per-handler local (unsynced) slots built
// once on the first miss and reused, so nothing goes through DebugManager
// (whose shared mesh pool reshuffles and blinks when a timed mesh is added).
// The ring is a unit mesh scaled by its slot so its geometry never
// regenerates mid-animation.
internal sealed class MissEffect
{
    private static readonly Random Rng = new();

    private Slot _root = null!;

    // Sparks: a PhotonDust burst. Rate 0; each enable of the emitter fires
    // BurstOnActivated particles, simulated in world space so moving the rig
    // for the next miss doesn't drag live sparks along.
    private Slot _sparkSlot = null!;
    private PD.ParticleSystemHelper.ParticleSystemComponents<UnlitMaterial> _sparks = null!;
    private PD.SphereEmitter _emitter = null!;
    private bool _pendingBurst;

    // Ring: a thin view-facing ring expanding to the sweep radius.
    private Slot _ringSlot = null!;
    private UnlitMaterial _ringMaterial = null!;

    private PinchMissEffectKind _active;
    private double _start;
    private float _duration;
    private float _radius;
    private float _scale;
    private colorX _color;

    public bool IsValid => _root != null && !_root.IsRemoved;

    public static void Trigger(InteractionHandler handler, ProximityGrabState state, in float3 origin, float scale, FingerType finger)
    {
        PinchMissEffectKind kind = ProximityGrabMod.PinchMissEffect;
        if (kind == PinchMissEffectKind.Off)
            return;
        if (kind == PinchMissEffectKind.Random)
            kind = Rng.Next(2) == 0 ? PinchMissEffectKind.Sparks : PinchMissEffectKind.Ring;
        if (state.MissEffect == null || !state.MissEffect.IsValid)
        {
            state.MissEffect?._root?.Destroy();
            state.MissEffect = Build(handler.World);
        }
        state.MissEffect.Start(handler, kind, origin, scale, FistGesture.PinchFingerColor(finger));
    }

    public static void Update(InteractionHandler handler, ProximityGrabState state)
    {
        MissEffect? effect = state.MissEffect;
        if (effect == null || !effect.IsValid)
            return;
        effect.Tick(handler);
    }

    private static MissEffect Build(World world)
    {
        var e = new MissEffect();
        e._root = world.AddLocalSlot("ProximityGrab MissEffect");

        // --- Sparks ---
        e._sparkSlot = e._root.AddSlot("Sparks");
        var ps = PD.ParticleSystemHelper.SetupParticleSystem<UnlitMaterial>(e._sparkSlot, singleSlot: true, setupEmitter: false, setupRotation: false, setupGravity: true);
        ps.System.MaxParticleCount.Value = 64;
        ps.Material.UseVertexColors.Value = true;
        ps.Material.BlendMode.Value = BlendMode.Additive;
        ps.Material.Texture.Target = e._sparkSlot.AttachTexture(OfficialAssets.Common.Particles.Basic);
        // Streaks along the direction of travel (the engine's own recipe for
        // legacy stretched billboards: Direction alignment + OrientByVelocity
        // with Up = Forward + a Y size multiplier).
        ps.Renderer.Alignment.Value = BillboardAlignment.Direction;
        var orient = ps.Style.AddModule<PD.OrientByVelocity>();
        orient.Up.Value = float3.Forward;
        var stretch = ps.Style.AddModule<PD.SizeModifier>();
        stretch.Multiplier.Value = new float3(1f, 4f, 1f);
        var drag = ps.Style.AddModule<PD.VelocityDrag>();
        drag.Drag.Value = 6f;
        var fade = ps.Style.AddModule<PD.AlphaOverLifetimeLinearGradient>();
        fade.AlphaOverLifetime.Append(new LinearKey<float>(0f, 1f));
        fade.AlphaOverLifetime.Append(new LinearKey<float>(0.6f, 0.8f));
        fade.AlphaOverLifetime.Append(new LinearKey<float>(1f, 0f));
        ps.Gravity.Gravity.Value = 0.6f;
        var emitter = ps.System.AddEmitter<PD.SphereEmitter>(e._sparkSlot);
        emitter.Rate.Value = 0f;
        emitter.Enabled = false;
        emitter.BurstOnActivatedMin.Value = 12f;
        emitter.BurstOnActivatedMax.Value = 18f;
        emitter.DirectionMode.Value = global::PhotonDust.SphereEmitterDirection.RadialUniform;
        e._sparks = ps;
        e._emitter = emitter;

        // --- Ring (unit radius; slot scale sets the size) ---
        e._ringSlot = e._root.AddSlot("Ring");
        var ring = e._ringSlot.AttachMesh<RingMesh, UnlitMaterial>();
        ring.mesh.InnerRadius.Value = 0.9f;
        ring.mesh.OuterRadius.Value = 1f;
        ring.mesh.Segments.Value = 48;
        ring.material.BlendMode.Value = BlendMode.Alpha;
        ring.material.Sidedness.Value = Sidedness.Double;
        ring.material.ZWrite.Value = ZWrite.Off;
        // Overlay queue, like DebugManager's meshes, so the hand can't hide it.
        ring.material.RenderQueue.Value = 3500;
        e._ringMaterial = ring.material;
        e._ringSlot.ActiveSelf = false;

        return e;
    }

    private void Start(InteractionHandler handler, PinchMissEffectKind kind, float3 origin, float scale, colorX color)
    {
        // A new miss replaces a ring still animating (live sparks keep
        // flying; they're independent particles).
        _ringSlot.ActiveSelf = false;
        _active = kind;
        _start = handler.Time.WorldTime;
        _duration = MathX.Max(ProximityGrabMod.PinchMissFlashSeconds, 0.05f);
        _radius = ProximityGrabMod.PrecisionMaxRadius * scale;
        _scale = scale;
        _color = color;

        if (kind == PinchMissEffectKind.Sparks)
        {
            BurstSparks(origin);
        }
        else
        {
            _ringSlot.GlobalPosition = origin;
            _ringSlot.ActiveSelf = true;
        }
        Tick(handler);
    }

    private void BurstSparks(in float3 origin)
    {
        float s = _scale;
        _sparkSlot.GlobalPosition = origin;
        _emitter.Radius.Value = 0.002f * s;
        // Speeds tuned so drag stops the sparks around the sweep radius.
        _sparks.Speed.MinValue.Value = 0.25f * s;
        _sparks.Speed.MaxValue.Value = 0.6f * s;
        _sparks.Size.MinValue.Value = 0.0015f * s;
        _sparks.Size.MaxValue.Value = 0.003f * s;
        _sparks.Lifetime.MinValue.Value = _duration * 0.6f;
        _sparks.Lifetime.MaxValue.Value = _duration * 1.2f;
        _sparks.Colors.MinValue.Value = _color;
        _sparks.Colors.MaxValue.Value = MathX.Lerp(_color, colorX.White, 0.7f);
        _sparks.Gravity.Gravity.Value = 0.6f * s;
        // Enabling the emitter fires the burst. If it's still enabled from the
        // last miss, disable now and re-enable next frame so OnEnabled fires.
        if (_emitter.Enabled)
        {
            _emitter.Enabled = false;
            _pendingBurst = true;
        }
        else
        {
            _emitter.Enabled = true;
        }
    }

    private void Tick(InteractionHandler handler)
    {
        if (_pendingBurst)
        {
            _pendingBurst = false;
            _emitter.Enabled = true;
        }

        if (_active != PinchMissEffectKind.Ring || !_ringSlot.ActiveSelf)
            return;
        float t = (float)((handler.Time.WorldTime - _start) / _duration);
        if (t >= 1f)
        {
            _ringSlot.ActiveSelf = false;
            return;
        }
        // Ease-out growth to the sweep radius, fading as it goes; always
        // faces the viewer.
        float grow = 1f - (1f - t) * (1f - t);
        float r = MathX.Lerp(0.15f, 1f, grow) * _radius;
        _ringSlot.GlobalScale = float3.One * r;
        float3 toView = handler.World.LocalUserViewPosition - _ringSlot.GlobalPosition;
        if (toView.SqrMagnitude > 1e-8f)
            _ringSlot.GlobalRotation = floatQ.LookRotation(toView.Normalized, float3.Up);
        _ringMaterial.TintColor.Value = _color.SetA(0.9f * (1f - t));
    }
}
