namespace FbApi.RetryService.Services;

public interface IRetryPolicyService
{
    long GetDelayMs(int retryCount, int baseDelayMs);
}

public class RetryPolicyService : IRetryPolicyService
{
    public long GetDelayMs(int retryCount, int baseDelayMs)
    {
        return baseDelayMs * (1L << retryCount);
    }
}
