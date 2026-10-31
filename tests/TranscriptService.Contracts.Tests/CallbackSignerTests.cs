using System.Security.Cryptography;
using System.Text;
using TranscriptService.Contracts.Callbacks;

namespace TranscriptService.Contracts.Tests;

public sealed class CallbackSignerTests
{
    private const string Secret = "secret";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"a":1}""");

    [Fact]
    public void Sign_KnownVector_MatchesHmacSha256Hex()
    {
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Body));

        CallbackSigner.Sign(Body, Secret).Should().Be(expected);
    }

    [Fact]
    public void Verify_OwnSignature_ReturnsTrue() =>
        CallbackSigner.Verify(Body, CallbackSigner.Sign(Body, Secret), Secret).Should().BeTrue();

    [Fact]
    public void Verify_TamperedBody_ReturnsFalse() =>
        CallbackSigner.Verify(Encoding.UTF8.GetBytes("""{"a":2}"""), CallbackSigner.Sign(Body, Secret), Secret).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("sha256=")]
    [InlineData("md5=abc")]
    [InlineData("sha256=zz")]
    public void Verify_MalformedSignature_ReturnsFalse(string signature) =>
        CallbackSigner.Verify(Body, signature, Secret).Should().BeFalse();
}
