namespace TakEngine.Crypto;

/// <summary>A secret or public key failed validation at the boundary (wrong length, not hex, not on the curve, out of range).</summary>
public sealed class InvalidKeyException(string message, Exception? innerException = null)
    : FormatException(message, innerException);
