using System;
using System.Collections.Generic;
using System.Text;

namespace TakEngine.Core.Cryptography;

/// <summary>
/// Implements NIP-19 Bech32 encoding and decoding for Nostr identities (npub, nsec).
/// </summary>
public static class Nip19
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private static readonly uint[] Generator = [0x3b6a57b2u, 0x26508e6du, 0x1ea119fau, 0x3d4233ddu, 0x2a1462b3u];

    public static string ToNpub(string hexPubKey)
    {
        byte[] bytes = Convert.FromHexString(hexPubKey);
        return Encode("npub", bytes);
    }

    public static string ToNsec(string hexPrivKey)
    {
        byte[] bytes = Convert.FromHexString(hexPrivKey);
        return Encode("nsec", bytes);
    }

    public static (string Hrp, string Hex) Decode(string bech32String)
    {
        if (string.IsNullOrWhiteSpace(bech32String))
            throw new ArgumentException("Input cannot be empty.", nameof(bech32String));

        bech32String = bech32String.Trim().ToLowerInvariant();
        int pos = bech32String.LastIndexOf('1');
        if (pos < 1 || pos + 7 > bech32String.Length)
            throw new FormatException("Invalid Bech32 separator position.");

        string hrp = bech32String[..pos];
        string dataPart = bech32String[(pos + 1)..];

        var values = new byte[dataPart.Length];
        for (int i = 0; i < dataPart.Length; i++)
        {
            int idx = Charset.IndexOf(dataPart[i]);
            if (idx < 0)
                throw new FormatException($"Invalid Bech32 character '{dataPart[i]}'.");
            values[i] = (byte)idx;
        }

        if (!VerifyChecksum(hrp, values))
            throw new FormatException("Invalid Bech32 checksum.");

        byte[] payload5Bit = new byte[values.Length - 6];
        Array.Copy(values, 0, payload5Bit, 0, payload5Bit.Length);

        byte[] bytes = ConvertBits(payload5Bit, 5, 8, false);
        return (hrp, Convert.ToHexStringLower(bytes));
    }

    public static string Encode(string hrp, byte[] data)
    {
        hrp = hrp.ToLowerInvariant();
        byte[] values = ConvertBits(data, 8, 5, true);
        byte[] checksum = CreateChecksum(hrp, values);

        var sb = new StringBuilder(hrp.Length + 1 + values.Length + checksum.Length);
        sb.Append(hrp);
        sb.Append('1');

        foreach (byte b in values)
            sb.Append(Charset[b]);

        foreach (byte b in checksum)
            sb.Append(Charset[b]);

        return sb.ToString();
    }

    private static uint Polymod(byte[] values)
    {
        uint chk = 1;
        foreach (byte v in values)
        {
            byte top = (byte)(chk >> 25);
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (int i = 0; i < 5; i++)
            {
                if (((top >> i) & 1) != 0)
                    chk ^= Generator[i];
            }
        }
        return chk;
    }

    private static byte[] HrpExpand(string hrp)
    {
        byte[] ret = new byte[hrp.Length * 2 + 1];
        for (int i = 0; i < hrp.Length; i++)
        {
            ret[i] = (byte)(hrp[i] >> 5);
            ret[i + hrp.Length + 1] = (byte)(hrp[i] & 31);
        }
        ret[hrp.Length] = 0;
        return ret;
    }

    private static bool VerifyChecksum(string hrp, byte[] values)
    {
        byte[] hrpExp = HrpExpand(hrp);
        byte[] enc = new byte[hrpExp.Length + values.Length];
        Array.Copy(hrpExp, 0, enc, 0, hrpExp.Length);
        Array.Copy(values, 0, enc, hrpExp.Length, values.Length);
        return Polymod(enc) == 1;
    }

    private static byte[] CreateChecksum(string hrp, byte[] values)
    {
        byte[] hrpExp = HrpExpand(hrp);
        byte[] enc = new byte[hrpExp.Length + values.Length + 6];
        Array.Copy(hrpExp, 0, enc, 0, hrpExp.Length);
        Array.Copy(values, 0, enc, hrpExp.Length, values.Length);
        uint mod = Polymod(enc) ^ 1;
        byte[] ret = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            ret[i] = (byte)((mod >> (5 * (5 - i))) & 31);
        }
        return ret;
    }

    private static byte[] ConvertBits(byte[] data, int fromBits, int toBits, bool pad)
    {
        int acc = 0;
        int bits = 0;
        int maxv = (1 << toBits) - 1;
        var ret = new List<byte>();

        foreach (byte value in data)
        {
            acc = (acc << fromBits) | value;
            bits += fromBits;
            while (bits >= toBits)
            {
                bits -= toBits;
                ret.Add((byte)((acc >> bits) & maxv));
            }
        }

        if (pad)
        {
            if (bits > 0)
                ret.Add((byte)((acc << (toBits - bits)) & maxv));
        }
        else if (bits >= fromBits || ((acc << (toBits - bits)) & maxv) != 0)
        {
            throw new FormatException("Invalid bit conversion padding.");
        }

        return ret.ToArray();
    }
}
