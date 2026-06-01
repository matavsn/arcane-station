using Content.Shared.Interaction;
using Content.Shared.Inventory;
using JetBrains.Annotations;
using Robust.Shared.Serialization;

namespace Content.Shared._Arcane.ERP.Fetishes;

/// <summary>
/// Context passed to every fetish condition check.
/// Carries all needed systems so conditions don't call IoCManager themselves.
/// </summary>
public readonly record struct FetishConditionContext(
    EntityUid Viewer,
    EntityUid? Target,
    IEntityManager EntMan,
    EntityLookupSystem Lookup,
    SharedTransformSystem Transform,
    SharedInteractionSystem Interaction,
    InventorySystem Inventory);

/// <summary>
/// Base class for all fetish conditions. Subclass and annotate with
/// concrete DataFields to define what triggers a fetish.
/// </summary>
[ImplicitDataDefinitionForInheritors, Serializable, NetSerializable]
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public abstract partial class FetishCondition
{
    /// <summary>
    /// True  = condition active  → SetPassiveSource.
    /// False = fires once/tick  → AddArousal(Impulse).
    /// </summary>
    public virtual bool IsPersistent => true;

    /// <summary>Whether this condition needs a <see cref="FetishConditionContext.Target"/> to evaluate.</summary>
    public virtual bool RequiresTarget => true;

    public abstract bool Check(FetishConditionContext ctx);
}

/// <summary>
/// Base for proximity-based conditions (range + optional LoS).
/// Self-targeting conditions derive from <see cref="FetishCondition"/> directly.
/// </summary>
[Serializable, NetSerializable]
public abstract partial class ProximityFetishCondition : FetishCondition
{
    [DataField]
    public float Range = 6f;

    [DataField]
    public bool RequiresLoS = false;

    public override bool RequiresTarget => true;
}
