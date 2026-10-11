using System;
using System.Collections.Generic;

namespace TakEngine.Abstractions;

/// <summary>The four game actions on the wire (pipeline/architecture.md "Wire format").</summary>
public enum GameActionType
{
    Join,
    Accept,
    Move,
    Resign,
}

public static class GameActionTypes
{
    public static string ToWire(this GameActionType type) => type switch
    {
        GameActionType.Join => "JOIN",
        GameActionType.Accept => "ACCEPT",
        GameActionType.Move => "MOVE",
        GameActionType.Resign => "RESIGN",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static bool TryParseWire(string? wire, out GameActionType type)
    {
        switch (wire)
        {
            case "JOIN": type = GameActionType.Join; return true;
            case "ACCEPT": type = GameActionType.Accept; return true;
            case "MOVE": type = GameActionType.Move; return true;
            case "RESIGN": type = GameActionType.Resign; return true;
            default: type = default; return false;
        }
    }
}

/// <summary>One <c>action_data</c> value: a string or an integer (D-041). Nothing else is allowed on the wire.</summary>
public readonly record struct ActionValue
{
    private ActionValue(string? text, long? integer)
    {
        Text = text;
        Integer = integer;
    }

    public string? Text { get; }

    public long? Integer { get; }

    public static ActionValue Of(string text) => new(text ?? throw new ArgumentNullException(nameof(text)), null);

    public static ActionValue Of(long integer) => new(null, integer);

    public override string ToString() => Text ?? Integer!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A game action as both peers sign and verify it (F-033). The inner <see cref="Signature"/> is BIP-340 over
/// <c>ActionDigest</c> with the player's one key (D-011), so a stored action verifies without its Nostr event.
/// <see cref="Turn"/> is 0 for JOIN and ACCEPT and the 1-based ply number for MOVE (D-040).
/// </summary>
public sealed record SignedGameAction(
    int Pv,
    GameActionType ActionType,
    Guid GameId,
    int Turn,
    string PlayerPubKey,
    string PrevStateHash,
    IReadOnlyDictionary<string, ActionValue> ActionData,
    string Signature)
{
    public const int CurrentPv = 1;

    /// <summary>The MOVE's PTN, or null for other action types.</summary>
    public string? Ptn => ActionData.TryGetValue("ptn", out ActionValue value) ? value.Text : null;

    /// <summary>An action with an empty signature, ready to be signed.</summary>
    public static SignedGameAction Unsigned(
        GameActionType actionType,
        Guid gameId,
        int turn,
        string playerPubKey,
        string prevStateHash,
        IEnumerable<KeyValuePair<string, ActionValue>>? actionData = null)
    {
        var data = new SortedDictionary<string, ActionValue>(StringComparer.Ordinal);
        if (actionData is not null)
        {
            foreach (var pair in actionData)
                data.Add(pair.Key, pair.Value);
        }
        return new SignedGameAction(CurrentPv, actionType, gameId, turn, playerPubKey, prevStateHash, data, "");
    }

    public static SignedGameAction UnsignedMove(Guid gameId, int turn, string playerPubKey, string prevStateHash, string ptn) =>
        Unsigned(GameActionType.Move, gameId, turn, playerPubKey, prevStateHash, [new("ptn", ActionValue.Of(ptn))]);
}
