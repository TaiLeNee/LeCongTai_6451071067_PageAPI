using System;
using System.Security.Cryptography;
using System.Text;

namespace FbApi.WebhookService.Services;

public class HmacValidationService
{
    public bool Validate(string payload, string signatureHeader, string appSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(appSecret))
            return false;

        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedSignature = signatureHeader[prefix.Length..];
        var actualSignature = ComputeHmacSha256(payload, appSecret);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualSignature),
            Encoding.UTF8.GetBytes(expectedSignature));
    }

    private static string ComputeHmacSha256(string payload, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
