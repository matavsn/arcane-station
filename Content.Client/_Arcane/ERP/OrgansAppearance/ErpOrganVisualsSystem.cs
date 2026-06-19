using Content.Client._Arcane.ERP.Preferences;
using Content.Client.Lobby;
using Content.Shared._Arcane.ERP;
using Content.Shared._Arcane.ERP.Organs;
using Content.Shared._Arcane.ERP.OrgansAppearance;
using Content.Shared._Arcane.ERP.Preferences;
using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Arcane.ERP.OrgansAppearance;

public sealed class ErpOrganVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly ClientErpOrganPreferencesManager _erpPrefs = default!;
    [Dependency] private readonly IClientPreferencesManager _prefs = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    // (slot, species) → prototype; built from erpOrganVisual prototypes at Initialize.
    private readonly Dictionary<(string slot, string species), ErpOrganVisualPrototype> _speciesLookup = new();
    // slot → fallback prototype (species list was empty).
    private readonly Dictionary<string, ErpOrganVisualPrototype> _fallbackLookup = new();
    // slot → layer key ("erp_{slot}"); derived from prototypes so new slots need no C#.
    private readonly Dictionary<string, string> _slotToLayerKey = new();

    // First clothing layer key in the humanoid sprite stack — organ layers insert before it.
    private const string FirstClothingLayer = "underwear";

    private ISawmill _log = default!;

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("erp.visuals.cl");

        BuildLookupTables();
        _proto.PrototypesReloaded += _ => BuildLookupTables();

        SubscribeLocalEvent<ErpOrganVisualsComponent, AfterAutoHandleStateEvent>(OnOrganState);
        SubscribeLocalEvent<ErpOrganVisualsComponent, ComponentShutdown>(OnOrganShutdown);

        SubscribeLocalEvent<HumanoidAppearanceComponent, HumanoidVisualStateUpdatedEvent>(OnHumanoidState);
        SubscribeLocalEvent<ArousalComponent, AfterAutoHandleStateEvent>(OnArousalState);

        // Editor preview: client-side dummy entity, no server state.
        SubscribeLocalEvent<HumanoidAppearanceComponent, ProfileLoadFinishedEvent>(OnPreviewProfileLoaded);
    }

    private void BuildLookupTables()
    {
        _speciesLookup.Clear();
        _fallbackLookup.Clear();
        _slotToLayerKey.Clear();

        foreach (var proto in _proto.EnumeratePrototypes<ErpOrganVisualPrototype>())
        {
            _slotToLayerKey.TryAdd(proto.Slot, proto.LayerKey);

            if (proto.Species.Count == 0)
                _fallbackLookup[proto.Slot] = proto;
            else
                foreach (var species in proto.Species)
                    _speciesLookup[(proto.Slot, species)] = proto;
        }
    }

    private ErpOrganVisualPrototype? GetProto(string slot, string species)
    {
        if (_speciesLookup.TryGetValue((slot, species), out var proto))
            return proto;

        _fallbackLookup.TryGetValue(slot, out var fallback);
        return fallback;
    }

    public void RefreshPreview(EntityUid uid, ErpOrganPreferences prefs)
    {
        if (!IsClientSide(uid))
            return;

        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        var humanoid = CompOrNull<HumanoidAppearanceComponent>(uid);
        var visuals = EnsureComp<ErpOrganVisualsComponent>(uid);
        visuals.Organs = FilterOrgansBySex(prefs.Organs, humanoid?.Sex ?? Sex.Male);

        ApplyOrganLayers((uid, visuals), humanoid, sprite);
    }

    private void OnPreviewProfileLoaded(Entity<HumanoidAppearanceComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        if (!IsClientSide(ent))
            return;

        if (!HasComp<EroticOrgansComponent>(ent))
            return;

        var slot = _prefs.Preferences?.SelectedCharacterIndex ?? 0;
        var organPrefs = _erpPrefs.GetSlot(slot);

        var visuals = EnsureComp<ErpOrganVisualsComponent>(ent);
        visuals.Organs = FilterOrgansBySex(organPrefs.Organs, ent.Comp.Sex);

        if (TryComp<SpriteComponent>(ent, out var sprite))
            ApplyOrganLayers((ent, visuals), ent.Comp, sprite);
    }

    private static Dictionary<string, ErpOrganConfig> FilterOrgansBySex(
        Dictionary<string, ErpOrganConfig> organs, Sex sex)
    {
        var result = new Dictionary<string, ErpOrganConfig>();
        foreach (var (slotId, cfg) in organs)
        {
            if (ErpOrganSlots.SexFilter.TryGetValue(slotId, out var allowed) && Array.IndexOf(allowed, sex) < 0)
                continue;
            result[slotId] = cfg;
        }
        return result;
    }

    private void OnOrganState(Entity<ErpOrganVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _log.Debug($"OnOrganState {ent}, organs={ent.Comp.Organs.Count}, covered={ent.Comp.CoveredSlots.Count}");
        if (!TryComp<SpriteComponent>(ent, out var sprite))
        {
            _log.Debug($"{ent} — no SpriteComponent");
            return;
        }

        ApplyOrganLayers(ent, CompOrNull<HumanoidAppearanceComponent>(ent), sprite);
    }

    private void OnHumanoidState(Entity<HumanoidAppearanceComponent> ent, ref HumanoidVisualStateUpdatedEvent args)
    {
        if (!TryComp<ErpOrganVisualsComponent>(ent, out var visuals))
            return;

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        ApplyOrganLayers((ent, visuals), ent.Comp, sprite);
    }

    private void OnArousalState(Entity<ArousalComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<ErpOrganVisualsComponent>(ent, out var visuals))
            return;

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        ApplyOrganLayers((ent, visuals), CompOrNull<HumanoidAppearanceComponent>(ent), sprite);
    }

    private void OnOrganShutdown(Entity<ErpOrganVisualsComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        foreach (var layerKey in _slotToLayerKey.Values)
        {
            if (!_sprite.LayerMapTryGet((ent, sprite), layerKey, out var index, false))
                continue;

            _sprite.LayerSetVisible((ent, sprite), index, false);
            _sprite.RemoveLayer((ent, sprite), index);
        }
    }

    private void ApplyOrganLayers(
        Entity<ErpOrganVisualsComponent> ent,
        HumanoidAppearanceComponent? humanoid,
        SpriteComponent sprite)
    {
        var phase   = CompOrNull<ArousalComponent>(ent)?.CurrentPhase ?? ArousalPhase.Calm;
        var species = humanoid?.Species ?? string.Empty;

        foreach (var slotId in ErpOrganSlots.All)
        {
            if (!_slotToLayerKey.TryGetValue(slotId, out var layerKey))
                continue;

            var proto = GetProto(slotId, species);
            if (proto == null)
            {
                // No RSI registered — remove any stale layer.
                if (_sprite.LayerMapTryGet((ent, sprite), layerKey, out var staleIdx, false))
                {
                    _sprite.LayerSetVisible((ent, sprite), staleIdx, false);
                    _sprite.RemoveLayer((ent, sprite), staleIdx);
                }
                continue;
            }

            if (!ent.Comp.Organs.TryGetValue(slotId, out var cfg))
            {
                if (_sprite.LayerMapTryGet((ent, sprite), layerKey, out var hiddenIdx, false))
                    _sprite.LayerSetVisible((ent, sprite), hiddenIdx, false);
                continue;
            }

            var rsiPath   = proto.Rsi;
            var stateName = ResolveStateName(proto, cfg, phase);
            var visible   = !ent.Comp.CoveredSlots.Contains(slotId)
                         && (!ent.Comp.HideWhenFlaccid.Contains(slotId) || phase >= ArousalPhase.Aroused);

            if (!_sprite.LayerMapTryGet((ent, sprite), layerKey, out var index, false))
            {
                int? insertIdx = null;
                if (_sprite.LayerMapTryGet((ent, sprite), FirstClothingLayer, out var clothingIdx, false))
                    insertIdx = clothingIdx;

                index = _sprite.AddLayer(
                    (ent, sprite),
                    new SpriteSpecifier.Rsi(new ResPath(rsiPath), stateName),
                    insertIdx);
                _sprite.LayerMapSet((ent, sprite), layerKey, index);
            }

            _log.Debug($"layer {slotId} state={stateName} visible={visible}");
            _sprite.LayerSetRsi((ent, sprite), index, new ResPath(rsiPath), stateName);
            _sprite.LayerSetColor((ent, sprite), index, cfg.Color ?? humanoid?.SkinColor ?? Color.FromHex("#C0967F"));
            _sprite.LayerSetVisible((ent, sprite), index, visible);
        }
    }

    private static string ResolveStateName(ErpOrganVisualPrototype proto, ErpOrganConfig cfg, ArousalPhase phase)
    {
        return proto.StateMode switch
        {
            ErpStateMode.Fixed => proto.FixedState,

            ErpStateMode.SizeString => Math.Clamp(cfg.Size, 1, 99).ToString(),

            ErpStateMode.SizeIndexed when proto.SizeStates.Count > 0 =>
                proto.SizeStates[Math.Clamp(cfg.Size - 1, 0, proto.SizeStates.Count - 1)],

            ErpStateMode.Arousal =>
                phase >= ArousalPhase.Aroused && proto.ArousalStates.TryGetValue(phase.ToString(), out var aroused)
                    ? aroused
                    : proto.FlaccidState,

            ErpStateMode.Variant =>
                proto.VariantAliases.TryGetValue(cfg.Variant, out var alias) ? alias : cfg.Variant,

            ErpStateMode.SizeIndexedArousal when proto.SizeStates.Count > 0 =>
                phase >= ArousalPhase.Aroused
                    ? proto.SizeStates[Math.Clamp(cfg.Size - 1, 0, proto.SizeStates.Count - 1)]
                    : proto.FlaccidState,

            _ => cfg.Variant,
        };
    }
}
