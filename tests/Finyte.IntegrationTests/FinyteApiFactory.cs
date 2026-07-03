using Finyte.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Finyte.IntegrationTests;

public sealed class FinyteApiFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Finyte", "Host=localhost;Port=5433;Database=finyte_test;Username=finyte;Password=finyte_dev_password");
        builder.UseSetting("DevAuth:Enabled", "true");
        builder.UseSetting("DevData:SeedOnStartup", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FinyteDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinyteDbContext>>();
            services.AddDbContext<FinyteDbContext>(x => x.UseInMemoryDatabase(databaseName));
        });
    }
}
