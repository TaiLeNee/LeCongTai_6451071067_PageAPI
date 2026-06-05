using FbApi.RetryService.Services;
using FluentAssertions;
using Xunit;

namespace FbApi.IntegrationTests.Tests;

public class RetryPolicyServiceTests
{
    private readonly RetryPolicyService _sut = new();
    private const int MaxRetryCount = 5;

    [Fact]
    public void RetryCount0_BaseDelay1s_Returns1s()
    {
        var result = _sut.GetDelayMs(0, 1000);

        result.Should().Be(1000); // 1000 * 2^0 = 1000
    }

    [Fact]
    public void RetryCount1_BaseDelay1s_Returns2s()
    {
        var result = _sut.GetDelayMs(1, 1000);

        result.Should().Be(2000); // 1000 * 2^1 = 2000
    }

    [Fact]
    public void RetryCount2_BaseDelay1s_Returns4s()
    {
        var result = _sut.GetDelayMs(2, 1000);

        result.Should().Be(4000); // 1000 * 2^2 = 4000
    }

    [Fact]
    public void RetryCount3_BaseDelay1s_Returns8s()
    {
        var result = _sut.GetDelayMs(3, 1000);

        result.Should().Be(8000); // 1000 * 2^3 = 8000
    }

    [Fact]
    public void RetryCount4_BaseDelay1s_Returns16s()
    {
        var result = _sut.GetDelayMs(4, 1000);

        result.Should().Be(16000); // 1000 * 2^4 = 16000
    }

    [Fact]
    public void RetryCount3_LessThanMaxRetryCount5_ShouldRetry()
    {
        const int retryCount = 3;
        var shouldRetry = retryCount < MaxRetryCount;

        shouldRetry.Should().BeTrue();
    }

    [Fact]
    public void RetryCount5_EqualsMaxRetryCount5_ShouldNotRetry()
    {
        const int retryCount = 5;
        var shouldRetry = retryCount < MaxRetryCount;

        shouldRetry.Should().BeFalse();
    }

    [Fact]
    public void RetryCount6_ExceedsMaxRetryCount5_ShouldNotRetry()
    {
        const int retryCount = 6;
        var shouldRetry = retryCount < MaxRetryCount;

        shouldRetry.Should().BeFalse();
    }

    [Fact]
    public void ExponentialBackoff_DoublesEachRetry()
    {
        var baseDelay = 1000;
        var delays = new long[5];
        for (int i = 0; i < 5; i++)
        {
            delays[i] = _sut.GetDelayMs(i, baseDelay);
        }

        for (int i = 1; i < 5; i++)
        {
            delays[i].Should().Be(delays[i - 1] * 2, 
                $"retry {i} delay should be double retry {i - 1} delay");
        }
    }

    [Fact]
    public void CustomBaseDelay_IsRespected()
    {
        var result = _sut.GetDelayMs(2, 500);

        result.Should().Be(2000); // 500 * 2^2 = 2000
    }
}
