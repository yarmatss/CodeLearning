using MassTransit;

namespace CodeLearning.Runner.Consumers;

public class SubmissionCreatedConsumerDefinition : ConsumerDefinition<SubmissionCreatedConsumer>
{
    private readonly int _maxConcurrentExecutions;
    private readonly int _maxDeliveryAttempts;
    private readonly TimeSpan _retryDelay;

    public SubmissionCreatedConsumerDefinition(IConfiguration configuration)
    {
        _maxConcurrentExecutions = configuration.GetValue("ExecutionSettings:MaxConcurrentExecutions", 5);
        _maxDeliveryAttempts = configuration.GetValue("ExecutionSettings:MaxDeliveryAttempts", 3);
        _retryDelay = TimeSpan.FromSeconds(configuration.GetValue("ExecutionSettings:RetryDelaySeconds", 5));

        EndpointName = "submissions";
        ConcurrentMessageLimit = _maxConcurrentExecutions;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<SubmissionCreatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = _maxConcurrentExecutions;

        // The delay keeps a brief outage (Docker, database) from using up every attempt at once.
        // Once the attempts run out the message moves to the error queue and SubmissionFaultConsumer fails the submission
        endpointConfigurator.UseMessageRetry(r => r.Interval(_maxDeliveryAttempts - 1, _retryDelay));

        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMq)
        {
            rabbitMq.SetQuorumQueue();
        }
    }
}
