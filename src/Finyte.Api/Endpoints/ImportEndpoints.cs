using Finyte.Api.Tenancy;
using Finyte.Data;
using Finyte.Data.Billing;
using Finyte.Data.Imports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").RequireAuthorization();
        group.MapGet("/", GetImports).WithName("GetImports");
        group.MapPost("/ofx", ImportOfx).WithName("ImportOfx")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(11 * 1024 * 1024), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = 10 * 1024 * 1024 });
        return app;
    }

    private static async Task<IResult> GetImports(TenantResolver tenantResolver, HttpContext httpContext,
        FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var imports = await dbContext.TransactionFileImports.AsNoTracking()
            .Where(x => x.TenantId == tenant.TenantId)
            .OrderByDescending(x => x.StartedAt).Take(100)
            .Select(x => new { x.Id, x.AccountId, x.FileName, x.Status, x.ImportedCount, x.SkippedCount, x.TotalCount, x.Error, x.StartedAt, x.CompletedAt })
            .ToListAsync(cancellationToken);
        return Results.Ok(imports);
    }

    private static async Task<IResult> ImportOfx([FromForm] Guid accountId, IFormFile file,
        TenantResolver tenantResolver, HttpContext httpContext, IBillingAccess billingAccess,
        TransactionFileImportService importService, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required before importing transactions.", statusCode: StatusCodes.Status402PaymentRequired);
        }
        if (file.Length == 0 || file.Length > 10 * 1024 * 1024)
        {
            return Results.BadRequest("Choose a non-empty OFX file of up to 10 MB.");
        }
        var fileName = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (fileName.Length > 255 || !Path.GetExtension(fileName).Equals(".ofx", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Choose an OFX transaction export with a filename of up to 255 characters.");
        }
        try
        {
            await using var stream = file.OpenReadStream();
            var result = await importService.Import(tenant.TenantId, accountId, fileName, stream, cancellationToken);
            return Results.Ok(new { result.Id, result.ImportedCount, result.SkippedCount, result.TotalCount });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }
}
