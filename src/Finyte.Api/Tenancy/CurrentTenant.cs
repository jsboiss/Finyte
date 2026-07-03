using Finyte.Core.Tenancy;

namespace Finyte.Api.Tenancy;

public sealed record CurrentTenant(string UserId, Guid TenantId, TenantRole Role);
