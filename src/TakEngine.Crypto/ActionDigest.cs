using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TakEngine.Abstractions;
using SHA256 = System.Security.Cryptography.SHA256;

namespace TakEngine.Crypto;

/// <summary>
/// The 32-byte digest a player signs for one game action (pipeline/architecture.md "Wire format", D-041):
/// SHA-256(UTF-8("tak/action/v1\n" + pv + "\n" + action_type + "\n" + game_id + "\n" + turn + "\n" + player_pubkey + "\n"
/// + prev_state_hash + "\n" + canonical(action_data))). Golden vectors pin it: changing anything here forks every game.
/// Lives in Crypto (not Core) so Transport's EnvelopeCodec can verify the inner signature without referencing Core.
/// </summary>
public static class ActionDigest
{
    private const string Domain = "tak/action/v1\n";

    private static readonly JsonWriterOptions CanonicalWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public static byte[] Compute(SignedGameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return SHA256.HashData(Encoding.UTF8.GetBytes(Preimage(action)));
    }

    /// <summary>The exact string that is hashed (exposed for vectors and debugging).</summary>
    public static string Preimage(SignedGameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var sb = new StringBuilder(Domain);
        sb.Append(action.Pv.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(action.ActionType.ToWire()).Append('\n');
        sb.Append(action.GameId.ToString("D")).Append('\n');
        sb.Append(action.Turn.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(action.PlayerPubKey).Append('\n');
        sb.Append(action.PrevStateHash).Append('\n');
        sb.Append(CanonicalActionData(action.ActionData));
        return sb.ToString();
    }

    /// <summary>Compact JSON, keys sorted ordinally, relaxed escaping, integers as plain digits (D-041).</summary>
    public static string CanonicalActionData(IReadOnlyDictionary<string, ActionValue> actionData)
    {
        ArgumentNullException.ThrowIfNull(actionData);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, CanonicalWriterOptions))
        {
            writer.WriteStartObject();
            foreach (var key in actionData.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                ActionValue value = actionData[key];
                writer.WritePropertyName(key);
                if (value.Text is not null)
                    writer.WriteStringValue(value.Text);
                else
                    writer.WriteNumberValue(value.Integer ?? 0);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Returns <paramref name="action"/> with <c>Signature</c> = BIP-340 over the digest (lowercase hex).</summary>
    public static SignedGameAction Sign(SecretKey key, SignedGameAction action, ReadOnlySpan<byte> aux32)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(action);
        byte[] signature = Schnorr.Sign(key, Compute(action), aux32);
        return action with { Signature = Convert.ToHexStringLower(signature) };
    }

    /// <summary>
    /// True only if <c>Signature</c> is a valid BIP-340 signature over the digest by <c>PlayerPubKey</c>.
    /// Never throws on malformed hex, keys or lengths: peer input returns false.
    /// </summary>
    public static bool Verify(SignedGameAction? action)
    {
        if (action is null || action.Signature is null || action.ActionData is null || action.PrevStateHash is null)
            return false;
        if (!PublicKey.TryFromHex(action.PlayerPubKey, out PublicKey? key))
            return false;
        if (action.Signature.Length != Schnorr.SignatureLength * 2)
            return false;
        byte[] signature = new byte[Schnorr.SignatureLength];
        if (Convert.FromHexString(action.Signature, signature, out _, out _) != OperationStatus.Done)
            return false;
        if (!Enum.IsDefined(action.ActionType))
            return false;
        return Schnorr.Verify(key, Compute(action), signature);
    }
}
