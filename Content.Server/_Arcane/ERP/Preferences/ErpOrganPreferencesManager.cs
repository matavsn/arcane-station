using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Arcane.ERP.Preferences;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Arcane.ERP.Preferences;

public sealed class ErpOrganPreferencesManager : IPostInjectInit
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;

    private readonly Dictionary<NetUserId, Dictionary<int, ErpOrganPreferences>> _cache = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true };

    public void PostInject()
    {
        _net.RegisterNetMessage<MsgErpOrganPreferences>();
        _net.RegisterNetMessage<MsgUpdateErpOrganPreferences>(HandleUpdate);

        _userDb.AddOnLoadPlayer(OnPlayerConnected);
        _userDb.AddOnFinishLoad(OnFinishLoad);
        _userDb.AddOnPlayerDisconnect(OnPlayerDisconnected);
    }

    private async Task OnPlayerConnected(ICommonSession session, CancellationToken cancel)
    {
        var slots = new Dictionary<int, ErpOrganPreferences>();
        _cache[session.UserId] = slots;

        var maxSlots = _cfg.GetCVar(CCVars.GameMaxCharacterSlots);
        for (var slot = 0; slot < maxSlots; slot++)
        {
            cancel.ThrowIfCancellationRequested();
            var json = await _db.GetErpOrganPreferencesAsync(session.UserId, slot);
            slots[slot] = Deserialize(json);
        }
    }

    private void OnFinishLoad(ICommonSession session)
    {
        if (!_cache.TryGetValue(session.UserId, out var slots))
            return;

        foreach (var slot in slots.Keys)
            SendToClient(session, slot);
    }

    private void OnPlayerDisconnected(ICommonSession session)
    {
        _cache.Remove(session.UserId);
    }

    public void SendToClient(ICommonSession session, int slot)
    {
        if (!_cache.TryGetValue(session.UserId, out var slots))
            return;

        var prefs = slots.TryGetValue(slot, out var p) ? p : ErpOrganPreferences.Default();
        var msg = new MsgErpOrganPreferences { Slot = slot, Preferences = prefs };
        _net.ServerSendMessage(msg, session.Channel);
    }

    public ErpOrganPreferences? GetCached(NetUserId userId, int slot)
    {
        if (_cache.TryGetValue(userId, out var slots) && slots.TryGetValue(slot, out var prefs))
            return prefs;
        return null;
    }

    private async void HandleUpdate(MsgUpdateErpOrganPreferences msg)
    {
        if (!_players.TryGetSessionByChannel(msg.MsgChannel, out var session))
            return;

        var prefs = msg.Preferences;
        var slot = msg.Slot;

        if (!_cache.TryGetValue(session.UserId, out var slots))
            _cache[session.UserId] = slots = new Dictionary<int, ErpOrganPreferences>();

        slots[slot] = prefs;

        var json = Serialize(prefs);
        await _db.SaveErpOrganPreferencesAsync(session.UserId, slot, json);
    }

    private static string Serialize(ErpOrganPreferences prefs)
        => JsonSerializer.Serialize(prefs, JsonOpts);

    private static ErpOrganPreferences Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ErpOrganPreferences.Default();

        try
        {
            return JsonSerializer.Deserialize<ErpOrganPreferences>(json, JsonOpts)
                   ?? ErpOrganPreferences.Default();
        }
        catch
        {
            return ErpOrganPreferences.Default();
        }
    }
}
