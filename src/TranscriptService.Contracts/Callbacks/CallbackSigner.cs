using System.Security.Cryptography;
using System.Text;

namespace TranscriptService.Contracts.Callbacks;

/// <summary>
/// HMAC-SHA256 signature of callback bodies, so a receiver can verify that a result came from the
/// API and was not altered. Header format: <c>X-Signature: sha256=&lt;lowercase hex&gt;</c>.
/// </summary>
public static class CallbackSigner
{
    public const string SignatureHeader = "X-Signature";
    public const string JobIdHeader = "X-Transcription-Id";

    private const string Prefix = "sha256=";

    public static string Sign(ReadOnlySpan<byte> body, string secret) =>
        Prefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    /// <summary>Checks a signature in constant time.</summary>
    public static bool Verify(ReadOnlySpan<byte> body, string? signature, string secret)
    {
        if (signature is null || !signature.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signature.AsSpan(Prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
