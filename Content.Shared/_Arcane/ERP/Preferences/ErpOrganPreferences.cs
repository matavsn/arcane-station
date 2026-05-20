using Robust.Shared.Serialization;

namespace Content.Shared._Arcane.ERP.Preferences;

/// <summary>
/// Per-character organ appearance configuration. Stored separately from HumanoidCharacterProfile.
/// </summary>
[Serializable, NetSerializable]
public sealed class ErpOrganPreferences
{
    /// <summary>Organ configs keyed by organ slot id ("penis", "vagina", "breasts", "testicles", "anus").</summary>
    public Dictionary<string, ErpOrganConfig> Organs { get; set; } = new();

    public ErpOrganConfig GetOrgan(string slotId)
        => Organs.TryGetValue(slotId, out var cfg) ? cfg : ErpOrganConfig.Default();

    public void SetOrgan(string slotId, ErpOrganConfig cfg)
        => Organs[slotId] = cfg;

    public static ErpOrganPreferences Default() => new();
}

[Serializable, NetSerializable]
public sealed class ErpOrganConfig
{
    /// <summary>Visual variant from RSI, e.g. "human", "knotted", "equine".</summary>
    public string Variant { get; set; } = "human";

    /// <summary>Size index 1–8.</summary>
    public int Size { get; set; } = 3;

    public static ErpOrganConfig Default() => new();
}

/// <summary>Organ slot ids used as keys in ErpOrganPreferences.</summary>
public static class ErpOrganSlots
{
    public const string Penis = "penis";
    public const string Vagina = "vagina";
    public const string Breasts = "breasts";
    public const string Testicles = "testicles";
    public const string Anus = "anus";

    public static readonly IReadOnlyList<string> All = [Penis, Vagina, Breasts, Testicles, Anus];
}
