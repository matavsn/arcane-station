using Content.Shared._Arcane.ERP;
using Content.Shared._Arcane.ERP.Fetishes;
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
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float TickInterval = 4f;
    private const float MaxTotalPassiveRate = 3.0f;
    private const float FatigueFullDuration = 120f; // seconds until rate → 0

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
        var ctx = BuildContext(uid);
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

            if (!EvaluateCondition(proto, uid, ctx))
                continue;

            var rate = Math.Min(proto.PassiveRate, MaxTotalPassiveRate - totalPositive);
            totalPositive += rate;

            if (proto.IsPersistent)
                newSources[$"fetish:{proto.ID}"] = rate;
            else
                _arousal.AddArousal(uid, proto.Impulse);
        }

        // Limits (negative) — no cap, processed after fetishes
        foreach (var limitId in fetish.Limits)
        {
            if (!_proto.TryIndex(limitId, out var proto))
                continue;

            if (proto.IsEventBased || proto.Condition == null)
                continue;

            if (!EvaluateCondition(proto, uid, ctx))
                continue;

            if (proto.IsPersistent)
                newSources[$"fetish:{proto.ID}"] = proto.PassiveRate; // expected to be negative
            else
                _arousal.AddArousal(uid, proto.Impulse);
        }

        SyncSources(uid, fetish, newSources, now);
    }

    // ── Condition evaluation ─────────────────────────────────────────────────

    private bool EvaluateCondition(FetishPrototype proto, EntityUid uid, FetishConditionContext ctx)
    {
        var cond = proto.Condition!;

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
        // TODO: Voyeurism — find watchers in range, give impulse
        // TODO: BeingWatched — give impulse to args.Target if watched
        // TODO: InteractionTagCondition — check tags against tag-based fetishes
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

    private FetishConditionContext BuildContext(EntityUid uid) =>
        new(uid, null, EntityManager, _lookup, _transform, _interaction, _inventory);

    private bool IsErpDisabled(EntityUid uid) =>
        TryComp<ErpStatusComponent>(uid, out var s) && s.Preference == ErpPreference.No;

    private bool IsErpConsenting(EntityUid uid) =>
        !TryComp<ErpStatusComponent>(uid, out var s) || s.Preference != ErpPreference.No;
}
