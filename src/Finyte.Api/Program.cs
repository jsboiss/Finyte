using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Finyte.Api.Auth;
using Finyte.Api.Billing;
using Finyte.Api.Endpoints;
using Finyte.Api.ProviderSync;
using Finyte.Api.Tenancy;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Quartz;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);
});

builder.Services.AddOpenApi();
builder.Services.AddFinyteData(builder.Configuration);
builder.Services
    .AddOptions<FiskilOptions>()
    .Bind(builder.Configuration.GetSection(FiskilOptions.SectionName));
builder.Services.AddHttpClient<IFiskilBankingClient, FiskilBankingClient>();
builder.Services
    .AddOptions<StripeOptions>()
    .Bind(builder.Configuration.GetSection(StripeOptions.SectionName))
    .Validate(StripeOptionsValidation.IsValid, "Stripe configuration contains an invalid key, price id, webhook secret, or URL.");
builder.Services.AddScoped<IStripeCheckoutService, StripeCheckoutService>();
builder.Services.AddScoped<IStripePortalService, StripePortalService>();
builder.Services.AddScoped<IStripeWebhookService, StripeWebhookService>();
builder.Services.AddScoped<IFiskilWebhookIngestor, FiskilWebhookIngestor>();
builder.Services.AddScoped<TenantResolver>();
builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(x => x.WaitForJobsToComplete = true);
builder.Services.AddHealthChecks().AddNpgSql(builder.Configuration.GetConnectionString("Finyte")!);
builder.Services.AddAuthorization();

var clerkAuthority = builder.Configuration["Clerk:Authority"];
var hasClerkAuthority = !string.IsNullOrWhiteSpace(clerkAuthority);
var hasDevAuth = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("DevAuth:Enabled");

if (hasDevAuth)
{
    builder.Services
        .AddAuthentication(DevAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, _ => { });
}
else if (hasClerkAuthority)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(x =>
        {
            x.Authority = clerkAuthority;
            x.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = true
            };
        });

}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();

if (hasClerkAuthority || hasDevAuth)
{
    app.UseAuthentication();
}

app.UseAuthorization();

app.MapEndpoints();

app.Run();

public partial class Program;
