﻿using Content.Server.Body.Components;
using Content.Server.Popups;
using Content.Shared.Examine;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server.ImmovableRod;

public sealed class ImmovableRodSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IMapManager _map = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // we are deliberately including paused entities. rod hungers for all
        foreach (var (rod, trans) in EntityManager.EntityQuery<ImmovableRodComponent, TransformComponent>(true))
        {
            rod.Accumulator += frameTime;

            if (rod.Accumulator > rod.Lifetime.TotalSeconds)
            {
                QueueDel(rod.Owner);
                return;
            }

            if (!rod.DestroyTiles)
                continue;
            if (!_map.TryGetGrid(trans.GridID, out var grid))
                continue;

            grid.SetTile(trans.Coordinates, Tile.Empty);
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ImmovableRodComponent, StartCollideEvent>(OnCollide);
        SubscribeLocalEvent<ImmovableRodComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ImmovableRodComponent, ExaminedEvent>(OnExamined);
    }

    private void OnComponentInit(EntityUid uid, ImmovableRodComponent component, ComponentInit args)
    {
        if (EntityManager.TryGetComponent(uid, out PhysicsComponent? phys))
        {
            phys.LinearDamping = 0f;
            phys.Friction = 0f;
            phys.BodyStatus = BodyStatus.InAir;

            if (!component.RandomizeVelocity)
                return;

            var xform = Transform(uid);
            var vel = component.DirectionOverride.Degrees switch
            {
                0f => _random.NextVector2(component.MinSpeed, component.MaxSpeed),
                _ => xform.WorldRotation.RotateVec(component.DirectionOverride.ToVec()) * _random.NextFloat(component.MinSpeed, component.MaxSpeed)
            };

            phys.ApplyLinearImpulse(vel);
            xform.LocalRotation = (vel - xform.WorldPosition).ToWorldAngle() + MathHelper.PiOver2;
        }
    }

    private void OnCollide(EntityUid uid, ImmovableRodComponent component, StartCollideEvent args)
    {
        var ent = args.OtherFixture.Body.Owner;

        if (_random.Prob(component.HitSoundProbability))
        {
            SoundSystem.Play(Filter.Pvs(uid), component.Sound.GetSound(), uid, component.Sound.Params);
        }

        if (HasComp<ImmovableRodComponent>(ent))
        {
            // oh god.
            var coords = Transform(uid).Coordinates;
            _popup.PopupCoordinates(Loc.GetString("immovable-rod-collided-rod-not-good"), coords, Filter.Pvs(uid));

            Del(uid);
            Del(ent);
            Spawn("Singularity", coords);

            return;
        }

        // gib em
        if (TryComp<BodyComponent>(ent, out var body))
        {
            component.MobCount++;

            _popup.PopupEntity(Loc.GetString("immovable-rod-penetrated-mob", ("rod", uid), ("mob", ent)), uid, Filter.Pvs(uid));
            body.Gib();
        }

        QueueDel(ent);
    }

    private void OnExamined(EntityUid uid, ImmovableRodComponent component, ExaminedEvent args)
    {
        if (component.MobCount == 0)
        {
            args.PushText(Loc.GetString("immovable-rod-consumed-none", ("rod", uid)));
        }
        else
        {
            args.PushText(Loc.GetString("immovable-rod-consumed-souls", ("rod", uid), ("amount", component.MobCount)));
        }
    }
}
