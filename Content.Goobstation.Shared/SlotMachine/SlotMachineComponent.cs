using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Audio;

namespace Content.Goobstation.Shared.SlotMachine;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlotMachineComponent : Component
{
    #region Sounds

    [DataField]
    public SoundSpecifier SpinSound = new SoundPathSpecifier("/Audio/_Goobstation/Machines/SlotMachine/slotmachine_spin.ogg");

    [DataField]
    public SoundSpecifier LoseSound = new SoundPathSpecifier("/Audio/Machines/buzz-two.ogg");

    [DataField]
    public SoundSpecifier SmallWinSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [DataField]
    public SoundSpecifier MediumWinSound = new SoundPathSpecifier("/Audio/Effects/Arcade/win.ogg");

    [DataField]
    public SoundSpecifier BigWinSound = new SoundPathSpecifier("/Audio/_Goobstation/Machines/SlotMachine/slotmachine_bigwin.ogg");

    [DataField]
    public SoundSpecifier JackPotWinSound = new SoundPathSpecifier("/Audio/_Goobstation/Machines/SlotMachine/slotmachine_jackpotwin.ogg");

    [DataField]
    public SoundSpecifier GodPotWinSound = new SoundPathSpecifier("/Audio/_Goobstation/Machines/SlotMachine/slotmachine_godpot.ogg");

    #endregion

    #region Chances

    [DataField, AutoNetworkedField]
    public float SmallWinChance = .20f;

    [DataField, AutoNetworkedField]
    public float MediumWinChance = .08f; // Arcane-edit

    [DataField, AutoNetworkedField]
    public float BigWinChance = .015f; // Arcane-edit

    [DataField, AutoNetworkedField]
    public float JackPotWinChance = .001f; // Arcane-edit

    [DataField, AutoNetworkedField]
    public float GodPotWinChance = .00002f; // Arcane-edit

    #endregion

    [DataField, AutoNetworkedField]
    public EntProtoId GodPotPrize = "WeaponShotgunHeavy";

    [DataField, AutoNetworkedField]
    public bool Emagged;

    #region Prize Amounts

    [DataField, AutoNetworkedField]
    public int SpinCost = 25; // Arcane-edit

    [DataField, AutoNetworkedField]
    public int SmallPrizeAmount = 25; // Arcane-edit

    [DataField, AutoNetworkedField]
    public int MediumPrizeAmount = 50; // Arcane-edit

    [DataField, AutoNetworkedField]
    public int BigPrizeAmount = 250; // Arcane-edit

    [DataField, AutoNetworkedField]
    public int JackPotPrizeAmount = 2500; // Arcane-edit

    #endregion

    #region DoAfter

    [DataField, AutoNetworkedField]
    public float DoAfterTime = 3.8f;

    [DataField, AutoNetworkedField]
    public bool IsSpinning;

    #endregion
}

[Serializable, NetSerializable]
public enum SlotMachineVisuals : byte
{
    Spinning
}
