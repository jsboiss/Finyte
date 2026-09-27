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
        services.AddScoped<Tenancy.TenantCalendars>();
        services.AddScoped<IOverviewProjector, OverviewProjector>();
        services.AddScoped<IProjectionDispatcher, ProjectionDispatcher>();
        services.AddScoped<IProjectionInvalidator, ProjectionInvalidator>();
        services.AddScoped<IBillingAccess, BillingAccess>();
        services.AddScoped<Imports.TransactionFileImportService>();
        services.AddScoped<Tagging.TransactionTagService>();
        services.AddScoped<Tagging.TagSuggestionService>();
        services.AddScoped<Transfers.InternalTransferService>();
        services.AddScoped<PayCycles.PayCycleQueries>();
        services.AddScoped<Recurring.RecurringPaymentService>();
        services.AddScoped<IFiskilBankingSyncService, FiskilBankingSyncService>();
        services.AddScoped<IProviderSyncRunner, ProviderSyncRunner>();
        services.AddScoped<IProviderSyncQueue, ProviderSyncQueue>();
        services.AddScoped<ISyncProjectionRefresher, SyncProjectionRefresher>();

        return services;
    }

    public static IServiceCollection AddFiskilProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<FiskilOptions>()
            .Bind(configuration.GetSection(FiskilOptions.SectionName));
        services.AddHttpClient<IFiskilBankingClient, FiskilBankingClient>();
        services.AddHttpClient<IFiskilLinkClient, FiskilLinkClient>();
        services.AddHttpClient<FiskilAccessTokenProvider>();
        services.AddSingleton<IFiskilAccessTokenProvider>(x => x.GetRequiredService<FiskilAccessTokenProvider>());
        return services;
    }
}
