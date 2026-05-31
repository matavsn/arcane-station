using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Arcane.ERP;

public sealed class OrgasmWeaknessSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OrgasmWeaknessComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<OrgasmWeaknessComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<OrgasmWeaknessComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.ExpiresAt)
                RemComp<OrgasmWeaknessComponent>(uid);
        }
    }

    private void OnRefreshSpeed(Entity<OrgasmWeaknessComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.SpeedModifier, ent.Comp.SpeedModifier);
    }

    private void OnShutdown(Entity<OrgasmWeaknessComponent> ent, ref ComponentShutdown args)
    {
        _speed.RefreshMovementSpeedModifiers(ent);
    }
}
