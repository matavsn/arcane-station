using Content.Shared._Arcane.ERP.Fetishes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Arcane.ERP.Fetishes;

/// <summary>
/// Server-only. Holds the character's active fetishes and limits,
/// and runtime tracking state for passive sources and fatigue.
/// Not networked — fetish data is private to the player.
/// </summary>
[RegisterComponent]
public sealed partial class FetishComponent : Component
{
    /// <summary>Fetishes that add arousal when their condition is met.</summary>
    [DataField]
    public List<ProtoId<FetishPrototype>> Fetishes = new();

    /// <summary>
    /// Turn-offs that actively drain arousal when their condition is met.
    /// PassiveRate must be negative. Drain stacks with normal decay — this is intentional.
    /// Attraction dislikes (sex/species) are handled separately as filters, not as limits.
    /// </summary>
    [DataField]
    public List<ProtoId<FetishPrototype>> Limits = new();

    /// <summary>If non-empty, target sex must be one of these values for target-based fetishes to trigger.</summary>
    [DataField]
    public HashSet<Sex> LikedSexes = new();

    /// <summary>Target sex values that block target-based fetishes even when otherwise liked.</summary>
    [DataField]
    public HashSet<Sex> DislikedSexes = new();

    /// <summary>If non-empty, target species must be one of these values for target-based fetishes to trigger.</summary>
    [DataField]
    public HashSet<ProtoId<SpeciesPrototype>> LikedSpecies = new();

    /// <summary>Target species values that block target-based fetishes even when otherwise liked.</summary>
    [DataField]
    public HashSet<ProtoId<SpeciesPrototype>> DislikedSpecies = new();

    /// <summary>Seconds between proximity evaluations. Staggered on init to spread server load.</summary>
    [DataField]
    public float UpdateInterval = 4f;

    // Runtime state (not serialized)

    /// <summary>Next server time at which this entity should be evaluated.</summary>
    public TimeSpan NextUpdate;

    /// <summary>Currently active passive sources. Key = "fetish:{id}", Value = effective rate/s after fatigue.</summary>
    public Dictionary<string, float> ActiveSources = new();

    /// <summary>When each source first became continuously active (for fatigue decay).</summary>
    public Dictionary<string, TimeSpan> SourceActiveSince = new();

    /// <summary>Sum of all active source rates (informational, useful for VV debug).</summary>
    public float CurrentTotalRate;
}
