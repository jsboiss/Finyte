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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();
app.MapEndpoints();

app.Run();
