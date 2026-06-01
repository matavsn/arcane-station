using Content.Shared._Arcane.ErpPanel.Requirements;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Arcane.ErpPanel;

[Prototype]
public sealed partial class PanelInteractionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<PanelInteractionCategoryPrototype> Category;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public List<string> Messages = new();

    [DataField]
    public List<string> SelfMessages = new();

    [DataField(required: true)]
    public SpriteSpecifier Icon = SpriteSpecifier.Invalid;

    [DataField]
    public List<ResPath> Sounds = new();

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    [DataField]
    public float Range = 1.5f;

    [DataField]
    public int UserArouse = 0;

    [DataField]
    public int TargetArouse = 0;

    [DataField]
    public List<ErpRequirement>? UserRequirements;

    [DataField]
    public List<ErpRequirement>? TargetRequirements;

    /// <summary>
    /// Semantic tags for this interaction (e.g. kiss, touch, tease, verbal, public, aftercare).
    /// Used by tag-based fetishes via <see cref="Content.Shared._Arcane.ERP.Fetishes.ErpInteractionOccurredEvent"/>.
    /// </summary>
    [DataField]
    public HashSet<string> Tags = new();
}

[Prototype]
public sealed partial class PanelInteractionCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;
}
