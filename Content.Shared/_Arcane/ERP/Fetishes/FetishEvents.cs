using Robust.Shared.Map;

namespace Content.Shared._Arcane.ERP.Fetishes;

/// <summary>
/// Raised on the server after a successful ERP interaction completes.
/// Used by FetishSystem for event-based fetishes (Voyeurism, BeingWatched, tag-based).
/// </summary>
[ByRefEvent]
public record struct ErpInteractionOccurredEvent(
    EntityUid User,
    EntityUid Target,
    IReadOnlySet<string> Tags,
    EntityCoordinates Location);
