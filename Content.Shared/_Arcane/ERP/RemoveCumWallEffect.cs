using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Arcane.ERP;

public sealed partial class RemoveCumWallEffect : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.QueueDeleteEntity(args.TargetEntity);
    }
}
