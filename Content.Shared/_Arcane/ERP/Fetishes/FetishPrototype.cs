using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Shared._Arcane.ERP.Fetishes;

[Prototype]
public sealed partial class FetishPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Desc;

    /// <summary>UI grouping: body, attraction, species, social, situational.</summary>
    [DataField(required: true)]
    public string Category = string.Empty;

    /// <summary>
    /// Passive arousal per second while condition holds.
    /// Negative values are turn-offs/limits that reduce arousal.
    /// </summary>
    [DataField]
    public float PassiveRate = 0.8f;

    /// <summary>One-shot arousal added per tick when condition is true and <see cref="IsPersistent"/> is false.</summary>
    [DataField]
    public float Impulse = 0f;

    /// <summary>
    /// True = SetPassiveSource while active.
    /// False = AddArousal(Impulse) each tick / event.
    /// </summary>
    [DataField]
    public bool IsPersistent = true;

    /// <summary>
    /// Fetishes handled via events (e.g. Voyeurism) skip the periodic tick entirely.
    /// FetishSystem processes them only in event handlers.
    /// </summary>
    [DataField]
    public bool IsEventBased = false;

    /// <summary>Null means no condition — only valid for event-based fetishes.</summary>
    [DataField]
    public FetishCondition? Condition;
}
