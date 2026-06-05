using FbApi.WebhookService.Services;
using FluentAssertions;
using Xunit;

namespace FbApi.IntegrationTests.Tests;

public class HmacValidationServiceTests
{
    private readonly HmacValidationService _sut = new();
    private const string AppSecret = "test_app_secret_12345";
    private const string Payload = "{\"object\":\"page\",\"entry\":[]}";

    private static string ComputeValidSignature(string payload, string appSecret)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        return "sha256=" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    [Fact]
    public void ValidSignature_ReturnsTrue()
    {
        var validSignature = ComputeValidSignature(Payload, AppSecret);

        var result = _sut.Validate(Payload, validSignature, AppSecret);

        result.Should().BeTrue();
    }

    [Fact]
    public void InvalidSignature_ReturnsFalse()
    {
        var invalidSignature = "sha256=0000000000000000000000000000000000000000000000000000000000000000";

        var result = _sut.Validate(Payload, invalidSignature, AppSecret);

        result.Should().BeFalse();
    }

    [Fact]
    public void MissingHeader_ReturnsFalse()
    {
        var result = _sut.Validate(Payload, null!, AppSecret);

        result.Should().BeFalse();
    }

    [Fact]
    public void EmptySignatureHeader_ReturnsFalse()
    {
        var result = _sut.Validate(Payload, "", AppSecret);

        result.Should().BeFalse();
    }

    [Fact]
    public void WrongFormat_NoSha256Prefix_ReturnsFalse()
    {
        var result = _sut.Validate(Payload, "abcd1234", AppSecret);

        result.Should().BeFalse();
    }

    [Fact]
    public void MissingAppSecret_ReturnsFalse()
    {
        var validSignature = ComputeValidSignature(Payload, AppSecret);

        var result = _sut.Validate(Payload, validSignature, null!);

        result.Should().BeFalse();
    }

    [Fact]
    public void DifferentPayload_ReturnsFalse()
    {
        var validSignature = ComputeValidSignature(Payload, AppSecret);
        var differentPayload = "{\"object\":\"page\",\"entry\":[{\"id\":\"123\"}]}";

        var result = _sut.Validate(differentPayload, validSignature, AppSecret);

        result.Should().BeFalse();
    }

    [Fact]
    public void CaseInsensitiveSha256Prefix_ReturnsTrue()
    {
        var validSignature = ComputeValidSignature(Payload, AppSecret);
        var result = _sut.Validate(Payload, validSignature, AppSecret);

        result.Should().BeTrue();
    }
}
