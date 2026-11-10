using CodeLearning.Core.Messages;
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

        EndpointName = MessageQueues.Submissions;
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
        // Errors caused by the data itself (e.g. text PostgreSQL rejects) would fail every attempt, so they fault at once
        endpointConfigurator.UseMessageRetry(r =>
        {
            r.Interval(_maxDeliveryAttempts - 1, _retryDelay);
            r.Ignore<Exception>(NonRetryableErrors.IsNonRetryable);
        });

        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMq)
        {
            rabbitMq.SetQuorumQueue();
        }
    }
}
