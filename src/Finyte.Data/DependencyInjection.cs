using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Finyte.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddFinyteData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Finyte") ?? throw new InvalidOperationException("Connection string 'Finyte' is not configured.");

        services.AddDbContext<FinyteDbContext>(x => x.UseNpgsql(connectionString));

        return services;
    }
}
