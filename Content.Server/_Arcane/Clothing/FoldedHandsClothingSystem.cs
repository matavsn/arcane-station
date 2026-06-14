using Content.Shared.Clothing;
using Content.Shared.Foldable;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory.VirtualItem;
using Robust.Shared.Containers;

namespace Content.Server._Arcane.Clothing;

public sealed class FoldedHandsClothingSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedVirtualItemSystem _virtualItem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FoldedHandsClothingComponent, FoldedEvent>(OnFolded);
        SubscribeLocalEvent<FoldedHandsClothingComponent, ClothingGotUnequippedEvent>(OnUnequipped);
    }

    private void OnFolded(Entity<FoldedHandsClothingComponent> ent, ref FoldedEvent args)
    {
        if (args.IsFolded)
            TryBlockHands(ent);
        else
            TryUnblockHands(ent);
    }

    private void OnUnequipped(Entity<FoldedHandsClothingComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        _virtualItem.DeleteInHandsMatching(args.Wearer, ent.Owner);
    }

    private void TryBlockHands(Entity<FoldedHandsClothingComponent> ent)
    {
        if (!TryGetWearer(ent, out var wearer))
            return;

        foreach (var hand in _hands.EnumerateHands(wearer))
        {
            _hands.TryDrop(wearer, hand);
            if (_virtualItem.TrySpawnVirtualItemInHand(ent.Owner, wearer, out var vItem))
                EnsureComp<UnremoveableComponent>(vItem.Value);
        }
    }

    private void TryUnblockHands(Entity<FoldedHandsClothingComponent> ent)
    {
        if (!TryGetWearer(ent, out var wearer))
            return;

        _virtualItem.DeleteInHandsMatching(wearer, ent.Owner);
    }

    private bool TryGetWearer(EntityUid item, out EntityUid wearer)
    {
        if (!_container.TryGetContainingContainer(item, out var cont))
        {
            wearer = default;
            return false;
        }

        wearer = cont.Owner;
        return true;
    }
}
