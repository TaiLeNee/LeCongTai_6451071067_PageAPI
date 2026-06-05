using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FbApi.BackendApi.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminApiKeyAttribute : Attribute, IAuthorizationFilter
{
    private const string HeaderName = "X-Admin-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedApiKey = configuration["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                error = new { code = "ADMIN_AUTH_NOT_CONFIGURED", message = "Admin API key is not configured." }
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var providedApiKey)
            || providedApiKey != expectedApiKey)
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                success = false,
                error = new { code = "UNAUTHORIZED", message = "Missing or invalid admin API key." }
            });
        }
    }
}
