using Content.Shared._Arcane.ERP;
using Content.Shared._Arcane.ERP.Fetishes;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Arcane.ERP.Fetishes;

public sealed class FetishSystem : EntitySystem
{
    [Dependency] private readonly ArousalSystem _arousal = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float TickInterval = 4f;
    private const float MaxTotalPassiveRate = 3.0f;
    private const float FatigueFullDuration = 120f; // seconds until rate → 0

    private static readonly IReadOnlySet<string> EmptyInteractionTags = new HashSet<string>();

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FetishComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ErpInteractionOccurredEvent>(OnErpInteraction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < TickInterval)
            return;

        _accumulator -= TickInterval;
        TickAll();
    }

    // ── Tick ─────────────────────────────────────────────────────────────────

    private void TickAll()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FetishComponent, ArousalComponent>();
        while (query.MoveNext(out var uid, out var fetish, out var arousal))
        {
            if (IsErpDisabled(uid))
            {
                ClearAllSources(uid, fetish);
                continue;
            }

            TickEntity(uid, fetish, now);
        }
    }

    private void TickEntity(EntityUid uid, FetishComponent fetish, TimeSpan now)
    {
        var ctx = BuildContext(uid, fetish);
        var newSources = new Dictionary<string, float>();
        var totalPositive = 0f;

        // Fetishes (positive) — apply rate cap, highest rate first
        foreach (var protoId in fetish.Fetishes)
        {
            if (!_proto.TryIndex(protoId, out var proto))
                continue;

            if (proto.IsEventBased || proto.Condition == null)
                continue;

            if (totalPositive >= MaxTotalPassiveRate)
                break;

            if (!TryFetish(proto, uid, fetish, ctx, isLimit: false))
                continue;

            var rate = Math.Min(proto.PassiveRate, MaxTotalPassiveRate - totalPositive);
            totalPositive += rate;

            if (proto.IsPersistent)
                newSources[$"fetish:{proto.ID}"] = rate;
            else
                _arousal.AddArousal(uid, proto.Impulse);
        }

        // Limits — turn-offs that actively drain arousal (passiveRate < 0) on top of normal decay.
        // DislikedSexes/DislikedSpecies are separate attraction filters handled in PassesTargetFilter.
        // If a limit should block gain rather than drain, that requires a suppress-multiplier path — future work.
        foreach (var limitId in fetish.Limits)
        {
            if (!_proto.TryIndex(limitId, out var proto))
                continue;

            if (proto.IsEventBased || proto.Condition == null)
                continue;

            if (!TryFetish(proto, uid, fetish, ctx, isLimit: true))
                continue;

            if (proto.IsPersistent)
                newSources[$"fetish:{proto.ID}"] = proto.PassiveRate; // expected to be negative
            else
                _arousal.AddArousal(uid, proto.Impulse);
        }

        SyncSources(uid, fetish, newSources, now);
    }

    // ── Condition evaluation ─────────────────────────────────────────────────

    private bool TryFetish(FetishPrototype proto, EntityUid uid, FetishComponent fetish, FetishConditionContext ctx, bool isLimit)
    {
        var cond = proto.Condition!;
        ctx = ctx with { IsLimit = isLimit };

        if (!cond.RequiresTarget)
            return cond.Check(ctx with { Target = null });

        var range = cond is ProximityFetishCondition prox ? prox.Range : 6f;
        var requiresLoS = cond is ProximityFetishCondition p && p.RequiresLoS;

        var nearby = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(uid, range, nearby);

        foreach (var target in nearby)
        {
            if (target == uid)
                continue;

            if (!HasComp<HumanoidAppearanceComponent>(target))
                continue;

            if (!IsErpConsenting(target))
                continue;

            if (!PassesTargetFilter(target, fetish, isLimit))
                continue;

            if (requiresLoS && !_interaction.InRangeUnobstructed(uid, target, range))
                continue;

            if (cond.Check(ctx with { Target = target }))
                return true;
        }

        return false;
    }

    // ── Source synchronisation ────────────────────────────────────────────────

    private void SyncSources(EntityUid uid, FetishComponent fetish,
        Dictionary<string, float> newSources, TimeSpan now)
    {
        // Remove sources that are no longer active
        foreach (var (src, _) in fetish.ActiveSources)
        {
            if (newSources.ContainsKey(src))
                continue;

            _arousal.RemovePassiveSource(uid, src);
            fetish.SourceActiveSince.Remove(src);
        }

        // Add or refresh sources with fatigue applied
        foreach (var (src, rate) in newSources)
        {
            if (!fetish.ActiveSources.ContainsKey(src))
                fetish.SourceActiveSince[src] = now;

            var since = fetish.SourceActiveSince.GetValueOrDefault(src, now);
            var effective = ApplyFatigue(rate, now - since);
            _arousal.SetPassiveSource(uid, src, effective);
        }

        fetish.ActiveSources = newSources;
        fetish.CurrentTotalRate = newSources.Values.Sum();
    }

    private static float ApplyFatigue(float rate, TimeSpan elapsed)
    {
        if (rate < 0f)
            return rate; // limits don't fatigue

        var t = (float)(elapsed.TotalSeconds / FatigueFullDuration);
        return rate * Math.Max(0f, 1f - t);
    }

    // ── Event-based fetishes ──────────────────────────────────────────────────

    private void OnErpInteraction(ref ErpInteractionOccurredEvent args)
    {
        if (args.Tags.Count == 0)
            return;

        TryApplyEventFetishes(args.User, args.Target, args.Tags);

        if (args.Target != args.User)
            TryApplyEventFetishes(args.Target, args.User, args.Tags);
    }

    private void TryApplyEventFetishes(EntityUid uid, EntityUid target, IReadOnlySet<string> tags)
    {
        if (!TryComp<FetishComponent>(uid, out var fetish))
            return;

        if (!HasComp<ArousalComponent>(uid))
            return;

        if (IsErpDisabled(uid) || !IsErpConsenting(target))
            return;

        var ctx = BuildContext(uid, fetish, tags) with { Target = target };

        foreach (var protoId in fetish.Fetishes)
        {
            if (!_proto.TryIndex(protoId, out var proto))
                continue;

            if (!proto.IsEventBased || proto.Condition == null)
                continue;

            if (!PassesTargetFilter(target, fetish, isLimit: false))
                continue;

            if (!proto.Condition.Check(ctx with { IsLimit = false }))
                continue;

            AddEventArousal(uid, proto);
        }

        foreach (var limitId in fetish.Limits)
        {
            if (!_proto.TryIndex(limitId, out var proto))
                continue;

            if (!proto.IsEventBased || proto.Condition == null)
                continue;

            if (!proto.Condition.Check(ctx with { IsLimit = true }))
                continue;

            AddEventArousal(uid, proto);
        }
    }

    private void AddEventArousal(EntityUid uid, FetishPrototype proto)
    {
        var amount = proto.Impulse != 0f ? proto.Impulse : proto.PassiveRate;
        if (amount == 0f)
            return;

        _arousal.AddArousal(uid, amount);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void OnShutdown(Entity<FetishComponent> ent, ref ComponentShutdown args)
    {
        ClearAllSources(ent, ent.Comp);
    }

    private void ClearAllSources(EntityUid uid, FetishComponent fetish)
    {
        foreach (var src in fetish.ActiveSources.Keys)
            _arousal.RemovePassiveSource(uid, src);

        fetish.ActiveSources.Clear();
        fetish.SourceActiveSince.Clear();
        fetish.CurrentTotalRate = 0f;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private FetishConditionContext BuildContext(EntityUid uid, FetishComponent fetish, IReadOnlySet<string>? interactionTags = null) =>
        new(uid, null, EntityManager, _lookup, _body, _transform, _interaction, _inventory,
            interactionTags ?? EmptyInteractionTags, false,
            fetish.LikedSexes, fetish.DislikedSexes, fetish.LikedSpecies, fetish.DislikedSpecies);

    private bool PassesTargetFilter(EntityUid target, FetishComponent fetish, bool isLimit)
    {
        if (!TryComp<HumanoidAppearanceComponent>(target, out var humanoid))
            return false;

        if (isLimit)
            return true;

        if (fetish.DislikedSexes.Contains(humanoid.Sex))
            return false;

        if (fetish.LikedSexes.Count > 0 && !fetish.LikedSexes.Contains(humanoid.Sex))
            return false;

        if (fetish.DislikedSpecies.Contains(humanoid.Species))
            return false;

        if (fetish.LikedSpecies.Count > 0 && !fetish.LikedSpecies.Contains(humanoid.Species))
            return false;

        return true;
    }

    private bool IsErpDisabled(EntityUid uid) =>
        TryComp<ErpStatusComponent>(uid, out var s) && s.Preference == ErpPreference.No;

    // Ask currently counts as opt-in for fetish checks.
    // If Ask becomes a per-interaction consent UI in the future, revisit: passive fetishes may need Yes-only.
    private bool IsErpConsenting(EntityUid uid) =>
        !TryComp<ErpStatusComponent>(uid, out var s)
        || s.Preference is ErpPreference.Yes or ErpPreference.Ask;
}
