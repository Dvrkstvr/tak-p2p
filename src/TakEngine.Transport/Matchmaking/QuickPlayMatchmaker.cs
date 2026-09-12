using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TakEngine.Abstractions;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Matchmaking;

public sealed class QuickPlayMatchmaker
{
    public const int EphemeralQuickPlayKind = 20001;
    public const int BroadcastTtlSeconds = 60;
    public const string QuickPlayTag = "tak_quickplay";
    public const string ClientVersion = "1.0";

    public static NostrEvent CreateBroadcastEvent(
        string ephemeralPubKey,
        BoardSize boardSize,
        IReadOnlyList<string> supportedRelays)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expiration = now + BroadcastTtlSeconds;

        var payload = new QuickPlayBroadcastPayload(
            EphemeralPubKey: ephemeralPubKey,
            Relays: supportedRelays,
            ClientVersion: ClientVersion);

        string content = JsonSerializer.Serialize(payload);

        var evt = new NostrEvent
        {
            Pubkey = ephemeralPubKey,
            CreatedAt = now,
            Kind = EphemeralQuickPlayKind,
            Tags =
            [
                ["t", QuickPlayTag],
                ["board_size", ((int)boardSize).ToString()],
                ["client_version", ClientVersion],
                ["expiration", expiration.ToString()]
            ],
            Content = content
        };

        evt.Id = evt.ComputeId();
        return evt;
    }

    public static bool TryParseBroadcastEvent(
        NostrEvent evt,
        out BoardSize boardSize,
        out QuickPlayBroadcastPayload? payload)
    {
        boardSize = BoardSize.Five;
        payload = null;

        if (evt.Kind != EphemeralQuickPlayKind)
            return false;

        bool hasQuickplayTag = false;
        string? sizeStr = null;

        foreach (var tag in evt.Tags)
        {
            if (tag.Count >= 2)
            {
                if (tag[0] == "t" && tag[1] == QuickPlayTag)
                    hasQuickplayTag = true;
                else if (tag[0] == "board_size")
                    sizeStr = tag[1];
            }
        }

        if (!hasQuickplayTag || string.IsNullOrEmpty(sizeStr))
            return false;

        if (!int.TryParse(sizeStr, out int sizeInt) || sizeInt is not (4 or 5 or 6))
            return false;

        boardSize = (BoardSize)sizeInt;

        try
        {
            payload = JsonSerializer.Deserialize<QuickPlayBroadcastPayload>(evt.Content);
            return payload != null;
        }
        catch
        {
            return false;
        }
    }

    public static ChallengeProposal CreateChallenge(
        BoardSize boardSize,
        string localPubKey,
        Guid? gameId = null)
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
        string seedHex = Convert.ToHexStringLower(randomBytes);

        return new ChallengeProposal(
            GameId: gameId ?? Guid.NewGuid(),
            BoardSize: boardSize,
            ProposerPubKey: localPubKey,
            RandomSeedHex: seedHex);
    }

    public static MatchSessionParams AcceptChallenge(
        ChallengeProposal proposal,
        string localPubKey,
        IReadOnlyList<string> relays)
    {
        (PlayerColor proposerColor, PlayerColor localColor) = ColorResolver.ResolveColors(
            proposal.RandomSeedHex,
            proposal.ProposerPubKey,
            localPubKey);

        return new MatchSessionParams(
            GameId: proposal.GameId,
            BoardSize: proposal.BoardSize,
            LocalColor: localColor,
            OpponentPubKey: proposal.ProposerPubKey,
            Relays: relays);
    }

    public static MatchSessionParams FinalizeChallengeOnProposer(
        ChallengeProposal proposal,
        string opponentPubKey,
        IReadOnlyList<string> relays)
    {
        (PlayerColor localColor, PlayerColor opponentColor) = ColorResolver.ResolveColors(
            proposal.RandomSeedHex,
            proposal.ProposerPubKey,
            opponentPubKey);

        return new MatchSessionParams(
            GameId: proposal.GameId,
            BoardSize: proposal.BoardSize,
            LocalColor: localColor,
            OpponentPubKey: opponentPubKey,
            Relays: relays);
    }
}
