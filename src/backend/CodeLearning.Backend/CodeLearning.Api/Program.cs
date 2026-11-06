using CodeLearning.Api;
using CodeLearning.Api.Extensions;
using CodeLearning.Application;
using CodeLearning.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment("Testing");

if (!isTesting)
{
    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .CreateBootstrapLogger();

    Log.Information("Starting CodeLearning API...");

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId());
}

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPresentation(builder.Configuration, builder.Environment);

var app = builder.Build();

var autoMigrate = app.Configuration.GetValue<bool>("Database:AutoMigrate", app.Environment.IsDevelopment());
if (autoMigrate)
{
    await app.ApplyPendingMigrationsAsync();
}

app.UseApiPipeline();

if (!isTesting)
{
    Log.Information("CodeLearning API started successfully");
}

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    if (!isTesting)
    {
        Log.Fatal(ex, "Application terminated unexpectedly");
    }
    throw;
}
finally
{
    if (!isTesting)
    {
        await Log.CloseAndFlushAsync();
    }
}
