using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Arcane.JoinQueue;

public enum QueueMiniGameKind : byte
{
    Gyruss,
    GoGoShitcurity,
    SpaceInvaders,
}

public readonly record struct QueueMiniGameLeaderboardEntry(QueueMiniGameKind Game, string PlayerName, int Score);

public sealed class QueueMiniGameScoreMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public QueueMiniGameKind Game { get; set; }
    public int Score { get; set; }

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Game = (QueueMiniGameKind) buffer.ReadByte();
        Score = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte) Game);
        buffer.Write(Score);
    }
}
