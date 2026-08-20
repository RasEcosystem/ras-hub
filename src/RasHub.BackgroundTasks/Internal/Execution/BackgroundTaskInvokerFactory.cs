using System.Collections.Concurrent;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.BackgroundTasks.Internal.Execution;

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

        return Cache.GetOrAdd(taskType,
            static type =>
            {
                var resultContracts = type.GetInterfaces()
                    .Where(candidate => candidate.IsGenericType &&
                                        candidate.GetGenericTypeDefinition() ==
                                        typeof(IBackgroundTask<>))
                    .ToArray();

                if (resultContracts.Length > 1)
                    throw new InvalidOperationException(
                        $"Background task '{type.FullName}' declares multiple result types.");

                var invokerType = resultContracts.Length == 0
                    ? typeof(BackgroundTaskInvoker<>).MakeGenericType(type)
                    : typeof(BackgroundTaskResultInvoker<,>).MakeGenericType(
                        type,
                        resultContracts[0].GetGenericArguments()[0]);

                return (IBackgroundTaskInvoker)(
                    Activator.CreateInstance(invokerType, true) ??
                    throw new InvalidOperationException(
                        $"Could not create invoker for '{type.FullName}'."));
            });
    }
}