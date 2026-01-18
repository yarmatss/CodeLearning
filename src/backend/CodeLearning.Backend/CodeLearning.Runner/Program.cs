using CodeLearning.Infrastructure.Data;
using CodeLearning.Runner.Services;
using CodeLearning.Runner.Services.Executors;
using CodeLearning.Runner.Workers;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

Log.Information("Starting CodeLearning Runner Service");

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId());

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var configuration = ConfigurationOptions.Parse(
            builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
        configuration.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(configuration);
    });

    // Services
    builder.Services.AddSingleton<ISubmissionQueue, SubmissionQueue>();
    builder.Services.AddSingleton<IDockerRunner, DockerRunner>();
    builder.Services.AddSingleton<ICodeExecutor, UniversalExecutor>();

    // Background Worker
    builder.Services.AddHostedService<ExecutionWorker>();

    var host = builder.Build();

    // Ensure workspace directory exists
    var workspaceBasePath = builder.Configuration["ExecutionSettings:WorkspaceBasePath"] 
        ?? "/tmp/submissions";
    
    if (!Directory.Exists(workspaceBasePath))
    {
        Directory.CreateDirectory(workspaceBasePath);
        Log.Information("Created workspace directory: {WorkspacePath}", workspaceBasePath);
    }

    Log.Information("CodeLearning Runner Service configured successfully");

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "CodeLearning Runner Service terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
