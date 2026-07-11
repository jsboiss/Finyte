using Microsoft.AspNetCore.Diagnostics;

namespace Finyte.Api.Tenancy;

public sealed class TenantExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ActiveOrganizationRequiredException => Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Active family required"),
            TenantNotProvisionedException => Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Family onboarding required"),
            _ => null
        };

        if (problem is null)
        {
            return false;
        }

        await problem.ExecuteAsync(httpContext);
        return true;
    }
}
