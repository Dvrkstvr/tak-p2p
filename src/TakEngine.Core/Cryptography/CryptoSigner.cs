using System;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace TakEngine.Core.Cryptography;

public sealed record KeyPair(string PublicKeyHex, string PrivateKeyHex);

public static class CryptoSigner
{
    private static readonly SecureRandom Random = new();

    public static KeyPair GenerateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(Random));
        AsymmetricCipherKeyPair keyPair = generator.GenerateKeyPair();

        var priv = (Ed25519PrivateKeyParameters)keyPair.Private;
        var pub = (Ed25519PublicKeyParameters)keyPair.Public;

        byte[] privBytes = priv.GetEncoded();
        byte[] pubBytes = pub.GetEncoded();

        return new KeyPair(
            Convert.ToHexStringLower(pubBytes),
            Convert.ToHexStringLower(privBytes));
    }

    public static string Sign(string privateKeyHex, string data)
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(data);
        return Sign(privateKeyHex, dataBytes);
    }

    public static string Sign(string privateKeyHex, byte[] data)
    {
        byte[] privBytes = Convert.FromHexString(privateKeyHex);
        var privKey = new Ed25519PrivateKeyParameters(privBytes, 0);

        var signer = new Ed25519Signer();
        signer.Init(true, privKey);
        signer.BlockUpdate(data, 0, data.Length);

        byte[] signature = signer.GenerateSignature();
        return Convert.ToHexStringLower(signature);
    }

    public static bool Verify(string publicKeyHex, string data, string signatureHex)
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(data);
        return Verify(publicKeyHex, dataBytes, signatureHex);
    }

    public static bool Verify(string publicKeyHex, byte[] data, string signatureHex)
    {
        try
        {
            byte[] pubBytes = Convert.FromHexString(publicKeyHex);
            byte[] sigBytes = Convert.FromHexString(signatureHex);

            var pubKey = new Ed25519PublicKeyParameters(pubBytes, 0);

            var verifier = new Ed25519Signer();
            verifier.Init(false, pubKey);
            verifier.BlockUpdate(data, 0, data.Length);

            return verifier.VerifySignature(sigBytes);
        }
        catch
        {
            return false;
        }
    }
}
