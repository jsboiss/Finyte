using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Finyte.Api.Endpoints;
using Finyte.Data;
using Quartz;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);
});

builder.Services.AddOpenApi();
builder.Services.AddFinyteData(builder.Configuration);
builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(x => x.WaitForJobsToComplete = true);
builder.Services.AddHealthChecks().AddNpgSql(builder.Configuration.GetConnectionString("Finyte")!);

var clerkAuthority = builder.Configuration["Clerk:Authority"];
var hasClerkAuthority = !string.IsNullOrWhiteSpace(clerkAuthority);

if (hasClerkAuthority)
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

    builder.Services.AddAuthorization();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();

if (hasClerkAuthority)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapEndpoints(hasClerkAuthority);

app.Run();
