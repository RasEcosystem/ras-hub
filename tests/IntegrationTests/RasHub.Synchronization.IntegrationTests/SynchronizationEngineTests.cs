using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Configuration;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.IntegrationTests;

public sealed class SynchronizationEngineTests
{
    [Fact]
    public async Task Enqueued_task_is_executed_and_completes_successfully()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton<ExecutionProbe>();

        builder.Services.AddScoped<
            IBackgroundTaskHandler<TestBackgroundTask>,
            TestBackgroundTaskHandler>();

        builder.Services.AddRasHubSynchronization(options =>
        {
            options.QueueCapacity = 16;
            options.WorkerCount = 1;
        });

        using var host = builder.Build();

        await host.StartAsync(cancellationToken);

        try
        {
            var engine = host.Services
                .GetRequiredService<ISynchronizationEngine>();

            var expectedValue = Guid.NewGuid();

            var handle = engine.Enqueue(
                new TestBackgroundTask(expectedValue));

            var result = await handle
                .WaitAsync(cancellationToken)
                .WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);

            var probe = host.Services
                .GetRequiredService<ExecutionProbe>();

            Assert.True(result.IsSucceeded);
            Assert.Equal(
                BackgroundTaskOutcome.Succeeded,
                result.Outcome);

            Assert.Equal(handle.Id, result.TaskId);
            Assert.Equal(1, result.AttemptCount);
            Assert.Null(result.Exception);

            Assert.Equal(1, probe.InvocationCount);
            Assert.Equal(expectedValue, probe.LastValue);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }
}

internal sealed record TestBackgroundTask(Guid Value)
    : IBackgroundTask;

internal sealed class TestBackgroundTaskHandler(ExecutionProbe probe)
    : IBackgroundTaskHandler<TestBackgroundTask>
{
    public Task ExecuteAsync(
        TestBackgroundTask task,
        CancellationToken cancellationToken)
    {
        probe.Record(task.Value);

        return Task.CompletedTask;
    }
}

internal sealed class ExecutionProbe
{
    private int _invocationCount;

    public int InvocationCount =>
        Volatile.Read(ref _invocationCount);

    public Guid LastValue { get; private set; }

    public void Record(Guid value)
    {
        LastValue = value;

        Interlocked.Increment(
            ref _invocationCount);
    }
}