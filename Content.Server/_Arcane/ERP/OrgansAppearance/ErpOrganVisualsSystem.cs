using Content.Server._Arcane.ERP.Preferences;
using Content.Server.Preferences.Managers;
using Content.Shared._Arcane.ERP;
using Content.Shared._Arcane.ERP.Organs;
using Content.Shared._Arcane.ERP.OrgansAppearance;
using Content.Shared._Arcane.ERP.Preferences;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Arcane.ERP.OrgansAppearance;

public sealed class ErpOrganVisualsSystem : EntitySystem
{
    [Dependency] private readonly ErpOrganPreferencesManager _erpPrefs = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;

    private ISawmill _log = default!;

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("erp.visuals");
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<EroticOrgansComponent, EroticOrgansSpawnedEvent>(OnOrgansSpawned);
        SubscribeLocalEvent<EroticOrganComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (!HasComp<HumanoidAppearanceComponent>(args.Entity) || !HasComp<EroticOrgansComponent>(args.Entity))
            return;

        var userId = args.Player.UserId;
        var slot = _prefs.GetPreferences(userId).SelectedCharacterIndex;
        RebuildOrganVisuals(args.Entity, userId, slot);
    }

    private void OnOrgansSpawned(Entity<EroticOrgansComponent> ent, ref EroticOrgansSpawnedEvent args)
    {
        if (!HasComp<HumanoidAppearanceComponent>(ent))
            return;

        // Only rebuild if a player is already controlling this entity.
        // If not, PlayerAttached will handle it later.
        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        var userId = actor.PlayerSession.UserId;
        var slot = _prefs.GetPreferences(userId).SelectedCharacterIndex;
        RebuildOrganVisuals(ent, userId, slot);
    }

    private void OnOrganRemoved(Entity<EroticOrganComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (!TryComp<OrganComponent>(ent, out var organ))
            return;

        if (!TryComp<ErpOrganVisualsComponent>(args.OldBody, out var visuals))
            return;

        visuals.Organs.Remove(organ.SlotId);
        Dirty(args.OldBody, visuals);
    }

    private void RebuildOrganVisuals(EntityUid uid, NetUserId userId, int slot)
    {
        var organPrefs = _erpPrefs.GetCached(userId, slot) ?? ErpOrganPreferences.Default();
        var eroticComp = CompOrNull<EroticOrgansComponent>(uid);

        var organs = new Dictionary<string, ErpOrganConfig>();
        foreach (var organ in _body.GetBodyOrganEntityComps<EroticOrganComponent>((uid, null)))
        {
            var slotId = organ.Comp2.SlotId;
            if (string.IsNullOrEmpty(slotId))
                continue;

            if (organPrefs.Organs.TryGetValue(slotId, out var cfg))
            {
                organs[slotId] = cfg;
            }
            else
            {
                // No saved preference: use species default variant if defined, otherwise generic default.
                var defaultVariant = eroticComp?.DefaultVariants.GetValueOrDefault(slotId) ?? "human";
                organs[slotId] = new ErpOrganConfig { Variant = defaultVariant };
            }
        }

        _log.Debug($"{uid} — {organs.Count} organs present, {organPrefs.Organs.Count} prefs");

        var visuals = EnsureComp<ErpOrganVisualsComponent>(uid);
        visuals.Organs = organs;
        visuals.HideWhenFlaccid = eroticComp?.HideWhenFlaccid ?? [];
        Dirty(uid, visuals);
    }
}
