using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TakEngine.Transport.Nostr;

/// <summary>
/// The one NIP-01 event serializer: <c>[0,pubkey,created_at,kind,tags,content]</c>, compact, UTF-8. Hand-written on
/// purpose (pipeline/architecture.md "Wire format", docs/decisions/0004): System.Text.Json escapes + &lt; &gt; &amp;, DEL and
/// astral characters even with relaxed escaping, so its ids differ from the ids relays compute.
/// Escapes: \" \\ \n \r \t \b \f; every other character below U+0020 as \u00xx (lowercase hex); everything else verbatim.
/// </summary>
public static class Nip01Serializer
{
    /// <summary>Throws <see cref="ArgumentException"/> if pubkey, content, tags or any tag value is null.</summary>
    public static string Serialize(string pubkey, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content)
    {
        ArgumentNullException.ThrowIfNull(pubkey);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(content);

        var sb = new StringBuilder(128 + content.Length);
        sb.Append("[0,");
        AppendString(sb, pubkey);
        sb.Append(',').Append(createdAt.ToString(CultureInfo.InvariantCulture));
        sb.Append(',').Append(kind.ToString(CultureInfo.InvariantCulture));
        sb.Append(",[");
        for (int i = 0; i < tags.Count; i++)
        {
            IReadOnlyList<string> tag = tags[i] ?? throw new ArgumentException($"Tag {i} is null.", nameof(tags));
            if (i > 0)
                sb.Append(',');
            sb.Append('[');
            for (int j = 0; j < tag.Count; j++)
            {
                if (j > 0)
                    sb.Append(',');
                AppendString(sb, tag[j] ?? throw new ArgumentException($"Tag {i} value {j} is null.", nameof(tags)));
            }
            sb.Append(']');
        }
        sb.Append("],");
        AppendString(sb, content);
        sb.Append(']');
        return sb.ToString();
    }

    public static string Serialize(NostrEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return Serialize(evt.Pubkey, evt.CreatedAt, evt.Kind, evt.Tags, evt.Content);
    }

    /// <summary>SHA-256 of the UTF-8 serialization: the 32-byte event id that BIP-340 signs.</summary>
    public static byte[] ComputeIdBytes(string pubkey, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(pubkey, createdAt, kind, tags, content)));

    /// <summary>The event id as 64 lowercase hex characters.</summary>
    public static string ComputeId(NostrEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return Convert.ToHexStringLower(ComputeIdBytes(evt.Pubkey, evt.CreatedAt, evt.Kind, evt.Tags, evt.Content));
    }

    private static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < ' ')
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
