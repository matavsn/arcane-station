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
/// Triggers from ERP panel interaction tags.
/// Event-based fetishes should use this condition with isEventBased: true.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class InteractionTagCondition : FetishCondition
{
    /// <summary>At least one of these tags must be present. Empty = ignored.</summary>
    [DataField]
    public HashSet<string> AnyTags = new();

    /// <summary>All of these tags must be present. Empty = ignored.</summary>
    [DataField]
    public HashSet<string> AllTags = new();

    /// <summary>If any of these tags is present, the condition fails.</summary>
    [DataField]
    public HashSet<string> ExcludedTags = new();

    public override bool RequiresTarget => false;

    public override bool IsPersistent => false;

    public override bool Check(FetishConditionContext ctx)
    {
        if (ctx.InteractionTags.Count == 0)
            return false;

        if (ExcludedTags.Overlaps(ctx.InteractionTags))
            return false;

        if (AllTags.Count > 0 && !AllTags.IsSubsetOf(ctx.InteractionTags))
            return false;

        if (AnyTags.Count > 0 && !AnyTags.Overlaps(ctx.InteractionTags))
            return false;

        return AnyTags.Count > 0 || AllTags.Count > 0;
    }
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
        foreach (var organ in ctx.Body.GetBodyOrganEntityComps<EroticOrganComponent>((target, null)))
        {
            if (!ctx.EntMan.HasComponent<T>(organ.Owner))
                continue;

            if (organ.Comp1.Visible)
                return true;
        }

        return false;
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

        foreach (var organ in ctx.Body.GetBodyOrganEntityComps<EroticOrganComponent>((target, null)))
        {
            if (!MatchesOrganType(ctx, organ.Owner))
                continue;

            if (!organ.Comp1.Visible)
                continue;

            if (MinSize.HasValue && organ.Comp1.Size < MinSize.Value)
                continue;

            if (MaxSize.HasValue && organ.Comp1.Size > MaxSize.Value)
                continue;

            return true;
        }

        return false;
    }

    private bool MatchesOrganType(FetishConditionContext ctx, EntityUid organ)
    {
        return OrganType switch
        {
            OrganFetishType.Any => true,
            OrganFetishType.Breasts => ctx.EntMan.HasComponent<BreastsOrganComponent>(organ),
            OrganFetishType.Penis => ctx.EntMan.HasComponent<PenisOrganComponent>(organ),
            OrganFetishType.Vagina => ctx.EntMan.HasComponent<VaginaOrganComponent>(organ),
            _ => false,
        };
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
        if (!HasVisibleOrgan(ctx, ctx.Viewer))
            return false;

        // At least one consenting humanoid must be nearby.
        var nearby = new HashSet<EntityUid>();
        ctx.Lookup.GetEntitiesInRange(ctx.Viewer, WitnessRange, nearby);

        foreach (var witness in nearby)
        {
            if (witness == ctx.Viewer)
                continue;

            if (!ctx.EntMan.TryGetComponent<HumanoidAppearanceComponent>(witness, out var humanoid))
                continue;

            if (ctx.EntMan.TryGetComponent<ErpStatusComponent>(witness, out var status)
                && status.Preference == ErpPreference.No)
                continue;

            if (!PassesTargetFilter(ctx, humanoid))
                continue;

            return true;
        }

        return false;
    }

    private static bool HasVisibleOrgan(FetishConditionContext ctx, EntityUid target)
    {
        foreach (var organ in ctx.Body.GetBodyOrganEntityComps<EroticOrganComponent>((target, null)))
        {
            if (organ.Comp1.Visible)
                return true;
        }

        return false;
    }

    private static bool PassesTargetFilter(FetishConditionContext ctx, HumanoidAppearanceComponent humanoid)
    {
        if (ctx.IsLimit)
            return true;

        if (ctx.DislikedSexes.Contains(humanoid.Sex))
            return false;

        if (ctx.LikedSexes.Count > 0 && !ctx.LikedSexes.Contains(humanoid.Sex))
            return false;

        if (ctx.DislikedSpecies.Contains(humanoid.Species))
            return false;

        if (ctx.LikedSpecies.Count > 0 && !ctx.LikedSpecies.Contains(humanoid.Species))
            return false;

        return true;
    }
}
