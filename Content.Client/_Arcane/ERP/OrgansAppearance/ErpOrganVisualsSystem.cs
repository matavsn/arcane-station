using Content.Client._Arcane.ERP.Preferences;
using Content.Client.Lobby;
using Content.Shared._Arcane.ERP.Organs;
using Content.Shared._Arcane.ERP.OrgansAppearance;
using Content.Shared._Arcane.ERP.Preferences;
using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Arcane.ERP.OrgansAppearance;

public sealed class ErpOrganVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly ClientErpOrganPreferencesManager _erpPrefs = default!;
    [Dependency] private readonly IClientPreferencesManager _prefs = default!;

    private static readonly Dictionary<string, string> SlotToLayer = new()
    {
        [ErpOrganSlots.Penis]     = "erp_penis",
        [ErpOrganSlots.Vagina]    = "erp_vagina",
        [ErpOrganSlots.Breasts]   = "erp_breasts",
        [ErpOrganSlots.Testicles] = "erp_testicles",
        [ErpOrganSlots.Anus]      = "erp_anus",
        [ErpOrganSlots.Butt]      = "erp_butt",
    };

    // Populated as per-organ RSI assets become available.
    private static readonly Dictionary<string, string> OrganRsiPath = new();

    private const string BreastsRsiBase = "/Textures/_Arcane/ERP/Mobs/Breasts/";
    private const string BreastsRsiFallback = BreastsRsiBase + "human.rsi";

    private static readonly Dictionary<string, string> SpeciesBreastRsi = new()
    {
        ["Human"]        = BreastsRsiBase + "human.rsi",
        ["Dwarf"]        = BreastsRsiBase + "human.rsi",
        ["Reptilian"]    = BreastsRsiBase + "lizard.rsi",
        ["Moth"]         = BreastsRsiBase + "moth.rsi",
        ["Tajaran"]      = BreastsRsiBase + "tajaran.rsi",
        ["Arachnid"]     = BreastsRsiBase + "arachnid.rsi",
        ["Demon"]        = BreastsRsiBase + "demon.rsi",
        ["HumanoidXeno"] = BreastsRsiBase + "xenos.rsi",
    };

    // First clothing layer key in the humanoid sprite stack — organ layers insert before it.
    private const string FirstClothingLayer = "underwear";

    private ISawmill _log = default!;

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("erp.visuals.cl");

        SubscribeLocalEvent<ErpOrganVisualsComponent, AfterAutoHandleStateEvent>(OnOrganState);
        SubscribeLocalEvent<ErpOrganVisualsComponent, ComponentShutdown>(OnOrganShutdown);

        SubscribeLocalEvent<HumanoidAppearanceComponent, HumanoidVisualStateUpdatedEvent>(OnHumanoidState);

        // Editor preview: client-side dummy entity, no server state
        SubscribeLocalEvent<HumanoidAppearanceComponent, ProfileLoadFinishedEvent>(OnPreviewProfileLoaded);
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
        // Skin color changed — refresh organ colors. Coverage already in the networked component.
        if (!TryComp<ErpOrganVisualsComponent>(ent, out var visuals))
            return;

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        ApplyOrganLayers((ent, visuals), ent.Comp, sprite);
    }

    private void OnOrganShutdown(Entity<ErpOrganVisualsComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        foreach (var layerKey in SlotToLayer.Values)
        {
            if (!_sprite.LayerMapTryGet((ent, sprite), layerKey, out var index, false))
                continue;

            _sprite.LayerSetVisible((ent, sprite), index, false);
            _sprite.RemoveLayer((ent, sprite), index); // also removes key from LayerMap
        }
    }

    private void ApplyOrganLayers(Entity<ErpOrganVisualsComponent> ent, HumanoidAppearanceComponent? humanoid, SpriteComponent sprite)
    {
        foreach (var slotId in ErpOrganSlots.All)
        {
            if (!SlotToLayer.TryGetValue(slotId, out var layerKey))
                continue;

            string rsiPath;
            if (slotId == ErpOrganSlots.Breasts)
            {
                var species = humanoid?.Species ?? string.Empty;
                rsiPath = SpeciesBreastRsi.TryGetValue(species, out var r) ? r : BreastsRsiFallback;
            }
            else if (!OrganRsiPath.TryGetValue(slotId, out rsiPath!))
            {
                // No RSI yet — if a stale layer exists, remove it.
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

            var stateName = BuildStateName(slotId, cfg, humanoid?.Species);
            var visible = !ent.Comp.CoveredSlots.Contains(slotId);

            if (!_sprite.LayerMapTryGet((ent, sprite), layerKey, out var index, false))
            {
                // Insert before the first clothing layer so organs render under clothing.
                // Each insertion pushes FirstClothingLayer up by one, so reading its index
                // fresh each time naturally queues organs in declaration order.
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

    private static string BuildStateName(string slotId, ErpOrganConfig cfg, string? species = null)
    {
        switch (slotId)
        {
            case ErpOrganSlots.Breasts:
                if (species == "HumanoidXeno")
                    return cfg.Size switch { 1 => "a", 2 => "b", _ => "c" };
                return cfg.Size switch { 1 => "aa", 2 => "b", 3 => "c", _ => "d" };
            case ErpOrganSlots.Butt:
                return $"butt_pair_{Math.Clamp(cfg.Size, 1, 5)}_0_FRONT";
            case ErpOrganSlots.Testicles:
                return "testicles_single_2_0_FRONT";
            case ErpOrganSlots.Anus:
                var aVariant = cfg.Variant is "" or "human" ? "donut" : cfg.Variant;
                return $"anus_{aVariant}_3_0_FRONT";
            case ErpOrganSlots.Vagina:
                return $"vagina_{cfg.Variant}_1_0_FRONT";
            default:
                return $"{slotId}_{cfg.Variant}_3_0_FRONT";
        }
    }
}
