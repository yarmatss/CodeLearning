using CodeLearning.Infrastructure;
using CodeLearning.Runner.Consumers;
using CodeLearning.Runner.Services;
using CodeLearning.Runner.Workers;
using MassTransit;

namespace CodeLearning.Runner;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRunner(IConfiguration configuration)
        {
            services.AddPersistence(configuration);

            services.AddSingleton<IDockerRunner, DockerRunner>();
            services.AddSingleton<ICodeExecutor, UniversalExecutor>();

            services.AddHostedService<StuckSubmissionSweeper>();
            services.AddHostedService<SandboxContainerCleanupService>();

            services.AddOptions<RabbitMqTransportOptions>().BindConfiguration("RabbitMqTransport");

            services.AddMassTransit(x =>
            {
                x.AddConsumer<SubmissionCreatedConsumer, SubmissionCreatedConsumerDefinition>();
                x.AddConsumer<SubmissionFaultConsumer>();

                x.UsingRabbitMq((context, cfg) => cfg.ConfigureEndpoints(context));
            });

            return services;
        }
    }
}
