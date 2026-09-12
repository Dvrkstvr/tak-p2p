using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using TakEngine.Abstractions;

namespace TakEngine.Transport.Matchmaking;

public sealed record QuickPlayBroadcastPayload(
    [property: JsonPropertyName("pubkey")] string EphemeralPubKey,
    [property: JsonPropertyName("relays")] IReadOnlyList<string> Relays,
    [property: JsonPropertyName("client_version")] string ClientVersion = "1.0");

public sealed record ChallengeProposal(
    [property: JsonPropertyName("game_id")] Guid GameId,
    [property: JsonPropertyName("board_size")] BoardSize BoardSize,
    [property: JsonPropertyName("proposer_pubkey")] string ProposerPubKey,
    [property: JsonPropertyName("random_seed")] string RandomSeedHex);

public sealed record ChallengeAcceptance(
    [property: JsonPropertyName("game_id")] Guid GameId,
    [property: JsonPropertyName("accepter_pubkey")] string AccepterPubKey,
    [property: JsonPropertyName("proposer_pubkey")] string ProposerPubKey,
    [property: JsonPropertyName("signature")] string Signature);

public sealed record MatchSessionParams(
    Guid GameId,
    BoardSize BoardSize,
    PlayerColor LocalColor,
    string OpponentPubKey,
    IReadOnlyList<string> Relays);

public static class ColorResolver
{
    public static (PlayerColor Peer1Color, PlayerColor Peer2Color) ResolveColors(
        string randomSeedHex,
        string peer1PubKey,
        string peer2PubKey)
    {
        // Lexicographically sort both public keys so calculation is independent of order
        string first = string.CompareOrdinal(peer1PubKey, peer2PubKey) <= 0 ? peer1PubKey : peer2PubKey;
        string second = string.Equals(first, peer1PubKey, StringComparison.Ordinal) ? peer2PubKey : peer1PubKey;

        string combined = $"{randomSeedHex.ToLowerInvariant()}:{first.ToLowerInvariant()}:{second.ToLowerInvariant()}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));

        // Even byte means 'first' is White, odd means 'first' is Black
        bool firstIsWhite = (hash[0] % 2 == 0);

        PlayerColor firstColor = firstIsWhite ? PlayerColor.White : PlayerColor.Black;
        PlayerColor secondColor = firstIsWhite ? PlayerColor.Black : PlayerColor.White;

        return (peer1PubKey == first) ? (firstColor, secondColor) : (secondColor, firstColor);
    }
}
