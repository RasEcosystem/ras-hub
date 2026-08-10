using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed partial class BackgroundTaskEngineBehaviorTests
{
    [Fact]
    public void Old_handle_dispose_does_not_remove_replacement_schedule()
    {
        using var host = CreateHost();
        var scheduler = host.Services
            .GetRequiredService<IBackgroundTaskScheduler>();
        var old = SchedulePayload(scheduler, "replaceable-schedule");

        Assert.True(scheduler.Remove(old.Handle.Id));

        ForceSchedulerCollection();
        Assert.False(old.Payload.IsAlive);

        using var replacement = scheduler.Schedule(
            old.Handle.Id,
            () => new RecordedTask(2),
            TimeSpan.FromDays(1));

        old.Handle.Dispose();

        var remaining = Assert.Single(scheduler.GetSchedules());
        Assert.Equal(replacement.Id, remaining.Id);

        replacement.Dispose();
        Assert.Empty(scheduler.GetSchedules());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        BackgroundTaskScheduleHandle Handle,
        WeakReference Payload) SchedulePayload(
        IBackgroundTaskScheduler scheduler,
        string scheduleId)
    {
        var payload = new object();
        var weakPayload = new WeakReference(payload);
        var handle = scheduler.Schedule(
            scheduleId,
            () =>
            {
                GC.KeepAlive(payload);
                return new RecordedTask(1);
            },
            TimeSpan.FromDays(1));

        return (handle, weakPayload);
    }

    private static void ForceSchedulerCollection()
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
