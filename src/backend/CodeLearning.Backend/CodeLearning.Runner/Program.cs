using CodeLearning.Infrastructure.Data;
using CodeLearning.Runner.Consumers;
using CodeLearning.Runner.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;

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

    // Services
    builder.Services.AddSingleton<IDockerRunner, DockerRunner>();
    builder.Services.AddSingleton<ICodeExecutor, UniversalExecutor>();

    builder.Services.AddOptions<RabbitMqTransportOptions>().BindConfiguration("RabbitMqTransport");

    builder.Services.AddMassTransit(x =>
    {
        x.AddConsumer<SubmissionCreatedConsumer, SubmissionCreatedConsumerDefinition>();
        x.AddConsumer<SubmissionFaultConsumer>();

        x.UsingRabbitMq((context, cfg) => cfg.ConfigureEndpoints(context));
    });

    var host = builder.Build();

    // Ensure workspace directory exists
    var workspaceBasePath = builder.Configuration["ExecutionSettings:WorkspaceBasePath"]
        ?? throw new InvalidOperationException("WorkspaceBasePath not configured");

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
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
