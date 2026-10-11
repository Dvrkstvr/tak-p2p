using TakEngine.Abstractions;
using TakEngine.Core.Cryptography;
using TakEngine.Transport.Matchmaking;

namespace TakEngine.Multiplayer.Handshake;

/// <summary>What an ACCEPT says (D-041 action data: black, host_nonce, komi, size, white).</summary>
public sealed record AcceptTerms(string White, string Black, byte[] HostNonce, BoardSize Size, int KomiHalves);

/// <summary>What a JOIN says (D-041, D-043 action data: guest_nonce, invite_proof).</summary>
/// <param name="InviteProof">Hex <see cref="ColorSeed.JoinProof"/>; the host compares it with its own.</param>
public sealed record JoinTerms(byte[] GuestNonce, string InviteProof);

/// <summary>Builds and reads JOIN and ACCEPT (turn 0, prev = the genesis hash of the board size). Pure.</summary>
public static class HandshakeMessages
{
    /// <param name="seedCommit">The invite's seed commitment, which only invite holders know (D-043).</param>
    public static SignedGameAction UnsignedJoin(Guid gameId, string guestPubKey, BoardSize size, byte[] guestNonce, string seedCommit) =>
        SignedGameAction.Unsigned(GameActionType.Join, gameId, 0, guestPubKey, StateHasher.ComputeGenesisHash(size),
        [
            new("guest_nonce", ActionValue.Of(Convert.ToHexStringLower(guestNonce))),
            new("invite_proof", ActionValue.Of(ColorSeed.JoinProof(seedCommit, guestPubKey))),
        ]);

    public static SignedGameAction UnsignedAccept(Guid gameId, string hostPubKey, AcceptTerms terms) =>
        SignedGameAction.Unsigned(GameActionType.Accept, gameId, 0, hostPubKey, StateHasher.ComputeGenesisHash(terms.Size),
        [
            new("white", ActionValue.Of(terms.White)),
            new("black", ActionValue.Of(terms.Black)),
            new("host_nonce", ActionValue.Of(Convert.ToHexStringLower(terms.HostNonce))),
            new("size", ActionValue.Of((int)terms.Size)),
            new("komi", ActionValue.Of(terms.KomiHalves)),
        ]);

    /// <summary>The terms of a JOIN, or null unless it carries exactly a 32-byte hex nonce and a proof string.</summary>
    public static JoinTerms? ReadJoin(SignedGameAction join)
    {
        if (join.ActionType != GameActionType.Join || join.Turn != 0 || join.ActionData.Count != 2)
            return null;
        if (!join.ActionData.TryGetValue("guest_nonce", out ActionValue nonce) || Nonce(nonce.Text) is not { } guestNonce
            || !join.ActionData.TryGetValue("invite_proof", out ActionValue proof) || proof.Text is null)
            return null;
        return new JoinTerms(guestNonce, proof.Text);
    }

    /// <summary>The terms of an ACCEPT, or null if a field is missing, extra or malformed.</summary>
    public static AcceptTerms? ReadAccept(SignedGameAction accept)
    {
        if (accept.ActionType != GameActionType.Accept || accept.Turn != 0 || accept.ActionData.Count != 5)
            return null;
        var d = accept.ActionData;
        if (!d.TryGetValue("white", out ActionValue white) || white.Text is null
            || !d.TryGetValue("black", out ActionValue black) || black.Text is null
            || !d.TryGetValue("host_nonce", out ActionValue nonce) || Nonce(nonce.Text) is not { } hostNonce
            || !d.TryGetValue("size", out ActionValue size) || size.Integer is not { } sizeValue
            || !d.TryGetValue("komi", out ActionValue komi) || komi.Integer is not { } komiValue)
            return null;
        if (sizeValue is < 0 or > 100 || komiValue is < 0 or > 100)
            return null; // out of any sane range; the guest then compares exact values with the invite

        return new AcceptTerms(white.Text, black.Text, hostNonce, (BoardSize)(int)sizeValue, (int)komiValue);
    }

    private static byte[]? Nonce(string? hex)
    {
        if (hex is null || hex.Length != 2 * ColorSeed.NonceLength || !hex.All(char.IsAsciiHexDigit))
            return null;
        return Convert.FromHexString(hex);
    }
}
