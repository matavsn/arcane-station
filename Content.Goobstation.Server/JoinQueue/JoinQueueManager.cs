using System.Linq;
using Content.Server.Connection;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Shared.CCVar;
using Content.Shared._Arcane.JoinQueue;
using Content.Goobstation.Shared.JoinQueue;
using Prometheus;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Content.Goobstation.Common.CCVar;
using Content.Server._RMC14.LinkAccount;
using Content.Server.Database;
using Content.Goobstation.Common.JoinQueue;

namespace Content.Goobstation.Server.JoinQueue;

/// <summary>
///     Manages new player connections when the server is full and queues them up, granting access when a slot becomes free
/// </summary>
public sealed class JoinQueueManager : IJoinQueueManager
{
    private static readonly Gauge QueueCount = Metrics.CreateGauge(
        "join_queue_total_count",
        "Amount of players in queue.");

    private static readonly Counter QueueBypassCount = Metrics.CreateCounter(
        "join_queue_bypass_count",
        "Amount of players who bypassed queue by privileges.");

    private static readonly Histogram QueueTimings = Metrics.CreateHistogram(
        "join_queue_timings",
        "Timings of players in queue",
        new HistogramConfiguration()
        {
            LabelNames = new[] { "type" },
            Buckets = Histogram.ExponentialBuckets(1, 2, 14),
        });


    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IConnectionManager _connection = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly LinkAccountManager _linkAccount = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IGameMapManager _gameMapManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private readonly List<ICommonSession> _queue = new();
    private readonly List<ICommonSession> _patronQueue = new();
    private readonly Dictionary<NetUserId, ICommonSession> _queuedSessions = new();
    private readonly Dictionary<NetUserId, Dictionary<QueueMiniGameKind, MiniGameScoreState>> _miniGameScores = new();

    /// <summary>
    ///     Rolling window of recent wait times in seconds for estimating queue wait.
    /// </summary>
    private readonly Queue<double> _recentWaitTimes = new();
    private const int MaxWaitTimeSamples = 20;

    /// <summary>
    ///     Holds queue positions for players who disconnected, allowing them to reclaim their spot if they reconnect within the grace period.
    /// </summary>
    private readonly Dictionary<NetUserId, QueueReservation> _reservations = new();

    private bool _isEnabled;
    private bool _patreonIsEnabled = true;

    /// <summary>
    ///     Interval for queue info refreshes
    /// </summary>
    private const float InfoRefreshIntervalSeconds = 30f;

    // Arcane-edit-start
    private const float MiniGameScoreUpdateIntervalSeconds = 1f;
    private const int MaxMiniGameScoreDeltaPerUpdate = 500;
    private float _infoRefreshTimer;
    private float _miniGameScoreBroadcastTimer;
    private bool _miniGameLeaderboardDirty;
    /// Arcane-edit-end

    public int PlayerInQueueCount => _queue.Count + _patronQueue.Count;
    public int ActualPlayersCount => _player.PlayerCount - PlayerInQueueCount;

    private readonly HashSet<NetUserId> _bypassUsers = new();

    public void Initialize()
    {
        _net.RegisterNetMessage<QueueUpdateMessage>();
        _net.RegisterNetMessage<QueueMiniGameScoreMessage>(OnMiniGameScore); // Arcane-edit

        _configuration.OnValueChanged(GoobCVars.QueueEnabled, OnQueueCVarChanged, true);
        _configuration.OnValueChanged(GoobCVars.PatreonSkip, OnPatronCvarChanged, true);
        _player.PlayerStatusChanged += OnPlayerStatusChanged;
        _userDb.AddOnFinishLoad(OnPlayerDataLoaded);
    }

    public void Update(float frameTime)
    {
        if (!_isEnabled || PlayerInQueueCount == 0)
            return;

        if (_miniGameLeaderboardDirty)
        {
            _miniGameScoreBroadcastTimer += frameTime;
            if (_miniGameScoreBroadcastTimer >= MiniGameScoreUpdateIntervalSeconds)
            {
                _miniGameLeaderboardDirty = false;
                _miniGameScoreBroadcastTimer = 0f;
                SendUpdateMessages();
                return;
            }
        }

        _infoRefreshTimer += frameTime;
        if (_infoRefreshTimer < InfoRefreshIntervalSeconds)
            return;

        _infoRefreshTimer = 0f;
        _miniGameLeaderboardDirty = false;
        SendUpdateMessages();
    }


    private void OnQueueCVarChanged(bool value)
    {
        _isEnabled = value;

        if (!value)
        {
            foreach (var session in _queue)
                session.Channel.Disconnect("Queue was disabled");
            foreach (var session in _patronQueue)
                session.Channel.Disconnect("Queue was disabled");
        }
    }

    private void OnPatronCvarChanged(bool value)
    {
        if (_patreonIsEnabled && !value && _patronQueue.Count > 0)
        {
            _queue.AddRange(_patronQueue);
            _queue.Sort(static (a, b) => a.ConnectedTime.CompareTo(b.ConnectedTime));
            _patronQueue.Clear();
        }
        _patreonIsEnabled = value;
    }


    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
        {
            var oldPosition = _queue.IndexOf(e.Session);
            var wasInQueue = oldPosition >= 0;
            var oldPatronPosition = _patronQueue.IndexOf(e.Session);
            var wasInPatronQueue = oldPatronPosition >= 0;

            if (wasInQueue)
                _queue.RemoveAt(oldPosition);
            if (wasInPatronQueue)
                _patronQueue.RemoveAt(oldPatronPosition);

            if (wasInQueue || wasInPatronQueue)
            {
                _queuedSessions.Remove(e.Session.UserId);
                _miniGameScores.Remove(e.Session.UserId);
            }

            if (e.OldStatus == SessionStatus.InGame)
                _bypassUsers.Remove(e.Session.UserId);

            if (wasInQueue || wasInPatronQueue)
            {
                var graceSeconds = _configuration.GetCVar(GoobCVars.QueueReconnectGraceSeconds);
                if (graceSeconds > 0)
                {
                    _reservations[e.Session.UserId] = new QueueReservation(
                        DateTime.UtcNow,
                        wasInPatronQueue ? oldPatronPosition : oldPosition,
                        wasInPatronQueue);
                }

                QueueTimings.WithLabels("Unwaited").Observe((DateTime.UtcNow - e.Session.ConnectedTime).TotalSeconds);
            }

            if (!wasInQueue && !wasInPatronQueue && e.OldStatus != SessionStatus.InGame) // Arcane-edit
                return;

            ProcessQueue(); // Arcane-edit
        }
        else if (e.NewStatus == SessionStatus.Connected)
        {
            if (!_isEnabled)
                SendToGame(e.Session);
        }
    }


    private async void OnPlayerDataLoaded(ICommonSession session)
    {
        if (!_isEnabled)
            return;

        var isPrivileged = await _connection.HasPrivilegedJoin(session.UserId);
        var currentOnline = _player.PlayerCount - 1 - _bypassUsers.Count;
        var haveFreeSlot = currentOnline < _configuration.GetCVar(CCVars.SoftMaxPlayers);

        if (haveFreeSlot) // Arcane-edit
        {
            SendToGame(session);
            _reservations.Remove(session.UserId);
            return;
        }

        if (_reservations.Remove(session.UserId, out var reservation))
        {
            var graceSeconds = _configuration.GetCVar(GoobCVars.QueueReconnectGraceSeconds);
            if ((DateTime.UtcNow - reservation.DisconnectTime).TotalSeconds <= graceSeconds)
            {
                if (reservation.IsPatron && !_patreonIsEnabled)
                {
                    _queue.Add(session);
                }
                else
                {
                    var queue = reservation.IsPatron ? _patronQueue : _queue;
                    queue.Insert(Math.Min(reservation.QueuePosition, queue.Count), session);
                }

                _queuedSessions[session.UserId] = session;
                ProcessQueue();
                return;
            }
        }

        // Arcane-edit-start
        if (isPrivileged && _patreonIsEnabled)
        {
            _patronQueue.Add(session);
            _queuedSessions[session.UserId] = session;
            ProcessQueue();
            return;
        }

        if (isPrivileged)
        {
            SendToGame(session);
            _bypassUsers.Add(session.UserId);
            QueueBypassCount.Inc();
            return;
        }

        _queue.Add(session);
        _queuedSessions[session.UserId] = session;
        // Arcane-edit-end
        ProcessQueue();
    }

    private void ProcessQueue() // Arcane-edit
    {
        var players = ActualPlayersCount;
        var softMax = _configuration.GetCVar(CCVars.SoftMaxPlayers);

        while (players < softMax && (_patronQueue.Count > 0 || _queue.Count > 0)) // Arcane-edit
        {
            // Arcane-edit-start
            var processPatron = _patronQueue.Count > 0 && (_patreonIsEnabled || _queue.Count == 0);
            var queue = processPatron ? _patronQueue : _queue;
            var session = queue[0];
            queue.RemoveAt(0);
            _queuedSessions.Remove(session.UserId);
            // Arcane-edit-end
            RecordWaitTime(session);
            SendToGame(session);
            QueueTimings.WithLabels("Waited")
                .Observe((DateTime.UtcNow - session.ConnectedTime).TotalSeconds); // Arcane-edit
            players++;
        }

        CleanupExpiredReservations();
        SendUpdateMessages();
        QueueCount.Set(PlayerInQueueCount); // Arcane-edit
    }

    private void RecordWaitTime(ICommonSession session)
    {
        var waitSeconds = (DateTime.UtcNow - session.ConnectedTime).TotalSeconds;
        _recentWaitTimes.Enqueue(waitSeconds);
        while (_recentWaitTimes.Count > MaxWaitTimeSamples)
            _recentWaitTimes.Dequeue();
    }

    private float GetEstimatedWaitForPosition(int position)
    {
        if (_recentWaitTimes.Count == 0)
            return -1f;

        var avg = _recentWaitTimes.Average();
        return (float) (avg * ((double) position / Math.Max(PlayerInQueueCount, 1)));
    }

    private void SendUpdateMessages()
    {
        var totalInQueue = _patronQueue.Count + _queue.Count;
        var currentPosition = 1;

        var mapName = _gameMapManager.GetSelectedMap()?.MapName ?? "Unknown";
        var gameMode = "Unknown";
        var roundDurationMinutes = 0;

        if (_entityManager.System<GameTicker>() is { } ticker)
        {
            var preset = ticker.CurrentPreset ?? ticker.Preset;
            if (preset != null)
                gameMode = Loc.GetString(preset.ModeTitle);

            if (ticker.RunLevel >= GameRunLevel.InRound)
            {
                var elapsed = _gameTiming.CurTime - ticker.RoundStartTimeSpan;
                roundDurationMinutes = (int) elapsed.TotalMinutes;
            }
        }

        var serverPlayerCount = ActualPlayersCount;
        var maxPlayerCount = _configuration.GetCVar(CCVars.SoftMaxPlayers);
        // Arcane-edit-start
        var miniGameLeaderboard = BuildMiniGameLeaderboard();

        var now = DateTime.UtcNow;
        var playerNames = new List<string>(totalInQueue);
        var playerWaitSeconds = new List<float>(totalInQueue);
        // Arcane-edit-end
        foreach (var session in _patronQueue)
        {
            playerNames.Add(session.Name);
            playerWaitSeconds.Add((float) (now - session.ConnectedTime).TotalSeconds); // Arcane-edit
        }

        foreach (var session in _queue)
        {
            playerNames.Add(session.Name);
            playerWaitSeconds.Add((float) (now - session.ConnectedTime).TotalSeconds); // Arcane-edit
        }

        for (var i = 0; i < _patronQueue.Count; i++, currentPosition++)
        {
            _patronQueue[i].Channel.SendMessage(new QueueUpdateMessage
            {
                Total = totalInQueue,
                Position = currentPosition,
                IsPatron = true,
                EstimatedWaitSeconds = GetEstimatedWaitForPosition(currentPosition),
                MapName = mapName,
                GameMode = gameMode,
                ServerPlayerCount = serverPlayerCount,
                MaxPlayerCount = maxPlayerCount,
                RoundDurationMinutes = roundDurationMinutes,
                YourName = _patronQueue[i].Name,
                PlayerNames = playerNames,
                // Arcane-edit-start
                PlayerWaitSeconds = playerWaitSeconds,
                MiniGameLeaderboard = miniGameLeaderboard,
                // Arcane-edit-end
            });
        }

        for (var i = 0; i < _queue.Count; i++, currentPosition++)
        {
            _queue[i].Channel.SendMessage(new QueueUpdateMessage
            {
                Total = totalInQueue,
                Position = currentPosition,
                IsPatron = false,
                EstimatedWaitSeconds = GetEstimatedWaitForPosition(currentPosition),
                MapName = mapName,
                GameMode = gameMode,
                ServerPlayerCount = serverPlayerCount,
                MaxPlayerCount = maxPlayerCount,
                RoundDurationMinutes = roundDurationMinutes,
                YourName = _queue[i].Name,
                PlayerNames = playerNames,
                // Arcane-edit-start
                PlayerWaitSeconds = playerWaitSeconds,
                MiniGameLeaderboard = miniGameLeaderboard,
                // Arcane-edit-end
            });
        }
    }

    // Arcane-edit-start
    private void OnMiniGameScore(QueueMiniGameScoreMessage message)
    {
        if (!Enum.IsDefined(typeof(QueueMiniGameKind), message.Game) ||
            !_queuedSessions.TryGetValue(message.MsgChannel.UserId, out var session))
            return;

        var score = Math.Clamp(message.Score, 0, GetMaxMiniGameScore(message.Game));
        if (!_miniGameScores.TryGetValue(session.UserId, out var scores))
        {
            scores = new Dictionary<QueueMiniGameKind, MiniGameScoreState>();
            _miniGameScores[session.UserId] = scores;
        }

        var now = _gameTiming.CurTime;
        var oldScore = 0;
        if (scores.TryGetValue(message.Game, out var oldState))
        {
            if (now - oldState.LastUpdateTime < TimeSpan.FromSeconds(MiniGameScoreUpdateIntervalSeconds))
                return;
            oldScore = oldState.Score;
        }

        if (oldScore >= score ||
            score - oldScore > MaxMiniGameScoreDeltaPerUpdate)
            return;

        scores[message.Game] = new MiniGameScoreState(score, now);
        if (!_miniGameLeaderboardDirty)
            _miniGameScoreBroadcastTimer = 0f;
        _miniGameLeaderboardDirty = true;
    }

    private List<QueueMiniGameLeaderboardEntry> BuildMiniGameLeaderboard()
    {
        var entries = new List<QueueMiniGameLeaderboardEntry>(15);
        foreach (var game in Enum.GetValues<QueueMiniGameKind>())
        {
            var candidates = new List<(string Name, int Score)>();
            foreach (var session in _patronQueue)
            {
                if (!_miniGameScores.TryGetValue(session.UserId, out var scores) ||
                    !scores.TryGetValue(game, out var state) ||
                    state.Score <= 0)
                    continue;
                candidates.Add((session.Name, state.Score));
            }
            foreach (var session in _queue)
            {
                if (!_miniGameScores.TryGetValue(session.UserId, out var scores) ||
                    !scores.TryGetValue(game, out var state) ||
                    state.Score <= 0)
                    continue;
                candidates.Add((session.Name, state.Score));
            }

            candidates.Sort(static (a, b) => b.Score.CompareTo(a.Score));
            for (var i = 0; i < Math.Min(5, candidates.Count); i++)
                entries.Add(new QueueMiniGameLeaderboardEntry(game, candidates[i].Name, candidates[i].Score));
        }

        return entries;
    }

    private static int GetMaxMiniGameScore(QueueMiniGameKind game)
    {
        return game switch
        {
            QueueMiniGameKind.Gyruss => 5000,
            QueueMiniGameKind.GoGoShitcurity => 10000,
            QueueMiniGameKind.SpaceInvaders => 6000,
            _ => 0,
        };
    }
    // Arcane-edit-end

    private void CleanupExpiredReservations()
    {
        var graceSeconds = _configuration.GetCVar(GoobCVars.QueueReconnectGraceSeconds);
        var now = DateTime.UtcNow;
        var expired = new List<NetUserId>();

        foreach (var (userId, reservation) in _reservations)
        {
            if ((now - reservation.DisconnectTime).TotalSeconds > graceSeconds)
                expired.Add(userId);
        }

        foreach (var userId in expired)
            _reservations.Remove(userId);
    }

    private void SendToGame(ICommonSession session)
    {
        // Arcane-edit-start
        _queuedSessions.Remove(session.UserId);
        _miniGameScores.Remove(session.UserId);
        // Arcane-edit-end
        Timer.Spawn(0, () => _player.JoinGame(session));
    }

    // Arcane-edit-start
    private sealed record QueueReservation(DateTime DisconnectTime, int QueuePosition, bool IsPatron);

    private readonly record struct MiniGameScoreState(int Score, TimeSpan LastUpdateTime);
    // Arcane-edit-end
}
