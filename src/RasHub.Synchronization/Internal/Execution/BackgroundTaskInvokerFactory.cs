using System.Collections.Concurrent;

namespace RasHub.Synchronization.Internal.Execution;

/// <summary>
///     Creates and caches the generic invoker required for each runtime task type.
/// </summary>
internal static class BackgroundTaskInvokerFactory
{
    private static readonly ConcurrentDictionary<Type, IBackgroundTaskInvoker> Cache =
        new();

    public static IBackgroundTaskInvoker Get(Type taskType)
    {
        ArgumentNullException.ThrowIfNull(taskType);

        return Cache.GetOrAdd(taskType, static type =>
        {
            var invokerType = typeof(BackgroundTaskInvoker<>)
                .MakeGenericType(type);

            return (IBackgroundTaskInvoker)(
                Activator.CreateInstance(invokerType, true) ??
                throw new InvalidOperationException(
                    $"Could not create invoker for '{type.FullName}'."));
        });
    }
}