using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Finyte.Data.Analytics;
using Finyte.Data.Billing;
using Finyte.Data.ProviderSync;

namespace Finyte.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddFinyteData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Finyte") ?? throw new InvalidOperationException("Connection string 'Finyte' is not configured.");

        services.AddDbContext<FinyteDbContext>(x => x.UseNpgsql(connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IOverviewProjector, OverviewProjector>();
        services.AddScoped<IProjectionDispatcher, ProjectionDispatcher>();
        services.AddScoped<IProjectionInvalidator, ProjectionInvalidator>();
        services.AddScoped<IBillingAccess, BillingAccess>();
        services.AddScoped<IFiskilBankingSyncService, FiskilBankingSyncService>();
        services.AddScoped<IProviderSyncRunner, ProviderSyncRunner>();
        services.AddScoped<ISyncProjectionRefresher, SyncProjectionRefresher>();

        return services;
    }
}
