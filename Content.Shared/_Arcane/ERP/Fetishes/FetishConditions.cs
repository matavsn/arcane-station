using Content.Shared._Arcane.ERP.Organs;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Arcane.ERP.Fetishes;

public enum OrganFetishType : byte
{
    Any,
    Breasts,
    Penis,
    Vagina,
}

/// <summary>
/// Triggers when the target has a visible (un-covered) erotic organ of the given type.
/// Uses <see cref="EroticOrganComponent.Visible"/> which is managed by the clothing coverage system.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ProximityOrganCondition : ProximityFetishCondition
{
    [DataField]
    public OrganFetishType OrganType = OrganFetishType.Any;

    public override bool Check(FetishConditionContext ctx)
    {
        if (ctx.Target is not { } target)
            return false;

        return OrganType switch
        {
            OrganFetishType.Any => HasVisibleOrgan<AnusOrganComponent>(ctx, target)
                                || HasVisibleOrgan<PenisOrganComponent>(ctx, target)
                                || HasVisibleOrgan<VaginaOrganComponent>(ctx, target)
                                || HasVisibleOrgan<BreastsOrganComponent>(ctx, target),
            OrganFetishType.Breasts => HasVisibleOrgan<BreastsOrganComponent>(ctx, target),
            OrganFetishType.Penis   => HasVisibleOrgan<PenisOrganComponent>(ctx, target),
            OrganFetishType.Vagina  => HasVisibleOrgan<VaginaOrganComponent>(ctx, target),
            _                       => false,
        };
    }

    // Checks if the target has at least one EroticOrganComponent of the marker type T that is currently visible.
    private static bool HasVisibleOrgan<T>(FetishConditionContext ctx, EntityUid target)
        where T : Component
    {
        // We look for an organ entity that has both T (type marker) and EroticOrganComponent (with Visible).
        // Body organs are child entities, so we query child entities of target.
        // For now: check via EroticOrganComponent children (body system required for full traversal;
        // simplified: check if target entity itself has the marker + visible — covers edge cases).
        // TODO: traverse actual body children when SharedBodySystem is wired into context.
        return ctx.EntMan.TryGetComponent<T>(target, out _)
            && ctx.EntMan.TryGetComponent<EroticOrganComponent>(target, out var organ)
            && organ.Visible;
    }
}

/// <summary>
/// Triggers when the target has an erotic organ whose <see cref="EroticOrganComponent.Size"/> is within bounds.
/// Size is a float where 1.0 = average. Large = >= 1.5, Small = <= 0.7.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class OrganSizeCondition : ProximityFetishCondition
{
    [DataField(required: true)]
    public OrganFetishType OrganType = OrganFetishType.Any;

    /// <summary>Minimum inclusive size multiplier. Null = no lower bound.</summary>
    [DataField]
    public float? MinSize;

    /// <summary>Maximum inclusive size multiplier. Null = no upper bound.</summary>
    [DataField]
    public float? MaxSize;

    public override bool Check(FetishConditionContext ctx)
    {
        if (ctx.Target is not { } target)
            return false;

        // Simplified: target carries the organ component directly (see TODO in ProximityOrganCondition).
        if (!ctx.EntMan.TryGetComponent<EroticOrganComponent>(target, out var organ))
            return false;

        if (!organ.Visible)
            return false;

        if (MinSize.HasValue && organ.Size < MinSize.Value)
            return false;

        if (MaxSize.HasValue && organ.Size > MaxSize.Value)
            return false;

        return true;
    }
}

/// <summary>Triggers when the target has the given biological sex.</summary>
[Serializable, NetSerializable]
public sealed partial class ProximitySexCondition : ProximityFetishCondition
{
    [DataField(required: true)]
    public Sex Sex;

    public override bool Check(FetishConditionContext ctx)
    {
        if (ctx.Target is not { } target)
            return false;

        return ctx.EntMan.TryGetComponent<HumanoidAppearanceComponent>(target, out var h)
               && h.Sex == Sex;
    }
}

/// <summary>Triggers when the target has the same biological sex as the viewer.</summary>
[Serializable, NetSerializable]
public sealed partial class SameSexCondition : ProximityFetishCondition
{
    public override bool Check(FetishConditionContext ctx)
    {
        if (ctx.Target is not { } target)
            return false;

        if (!ctx.EntMan.TryGetComponent<HumanoidAppearanceComponent>(ctx.Viewer, out var vComp))
            return false;

        if (!ctx.EntMan.TryGetComponent<HumanoidAppearanceComponent>(target, out var tComp))
            return false;

        return vComp.Sex == tComp.Sex;
    }
}

/// <summary>Triggers when the target belongs to one of the listed species.</summary>
[Serializable, NetSerializable]
public sealed partial class ProximitySpeciesCondition : ProximityFetishCondition
{
    [DataField(required: true)]
    public HashSet<ProtoId<SpeciesPrototype>> Species = new();

    public override bool Check(FetishConditionContext ctx)
    {
        if (ctx.Target is not { } target)
            return false;

        return ctx.EntMan.TryGetComponent<HumanoidAppearanceComponent>(target, out var h)
               && Species.Contains(h.Species);
    }
}

/// <summary>
/// Triggers when the viewer has at least one visible erotic organ AND
/// a consenting entity is within <see cref="WitnessRange"/>.
/// (Exhibitionism.)
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SelfNudityCondition : FetishCondition
{
    [DataField]
    public float WitnessRange = 6f;

    public override bool RequiresTarget => false;

    public override bool Check(FetishConditionContext ctx)
    {
        // Viewer must themselves be "naked" — has a visible organ.
        if (!ctx.EntMan.TryGetComponent<EroticOrganComponent>(ctx.Viewer, out var organ) || !organ.Visible)
            return false;

        // At least one consenting humanoid must be nearby.
        var nearby = new HashSet<EntityUid>();
        ctx.Lookup.GetEntitiesInRange(ctx.Viewer, WitnessRange, nearby);

        foreach (var witness in nearby)
        {
            if (witness == ctx.Viewer)
                continue;

            if (!ctx.EntMan.HasComponent<HumanoidAppearanceComponent>(witness))
                continue;

            if (ctx.EntMan.TryGetComponent<ErpStatusComponent>(witness, out var status)
                && status.Preference == ErpPreference.No)
                continue;

            return true;
        }

        return false;
    }
}
