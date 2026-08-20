using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Configuration;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed class BackgroundTaskEngineTests
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

        builder.Services.AddRasHubBackgroundTasks(options =>
        {
            options.SynchronizationQueueCapacity = 16;
            options.SynchronizationWorkerCount = 1;
        });

        using var host = builder.Build();

        await host.StartAsync(cancellationToken);

        try
        {
            var engine = host.Services
                .GetRequiredService<IBackgroundTaskEngine>();

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

            BackgroundTaskEngineStatistics statistics;
            do
            {
                statistics = engine.GetStatistics();
                if (statistics.SynchronizationCompletedTasks == 0)
                    await Task.Delay(10, cancellationToken);
            } while (statistics.SynchronizationCompletedTasks == 0);

            Assert.Equal(1, statistics.SynchronizationCompletedTasks);
            Assert.True(statistics.SynchronizationQueueHighWaterMark >= 1);
            Assert.True(statistics.StartedAt <= DateTimeOffset.UtcNow);

            Assert.Equal(1, probe.InvocationCount);
            Assert.Equal(expectedValue, probe.LastValue);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Result_task_returns_typed_value_to_waiting_caller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<ResultBackgroundTask, Guid>,
            ResultBackgroundTaskHandler>();
        builder.Services.AddRasHubBackgroundTasks();
        using var host = builder.Build();
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = host.Services
                .GetRequiredService<IBackgroundTaskEngine>();
            var expected = Guid.NewGuid();
            var result = await engine.Enqueue(
                    new ResultBackgroundTask(expected))
                .WaitAsync(cancellationToken);

            Assert.True(result.IsSucceeded);
            Assert.Equal(expected, result.GetValue<Guid>());
            Assert.Throws<InvalidOperationException>(() =>
                result.GetValue<string>());
            Assert.Null(engine.GetTask(result.TaskId)?.LastError);
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

internal sealed record ResultBackgroundTask(Guid Value)
    : IBackgroundTask<Guid>;

internal sealed class ResultBackgroundTaskHandler
    : IBackgroundTaskHandler<ResultBackgroundTask, Guid>
{
    public Task<Guid> ExecuteAsync(
        ResultBackgroundTask task,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(task.Value);
    }
}
