using System.Reflection;
using Quartz;

namespace TaxVision.Reminder.Tests.Scheduling;

/// <summary>
/// <c>IScheduler</c> falso, por proxy porque la interfaz es enorme. Como dos réplicas en carrera: dice que no hay
/// trigger al reprogramar y choca al crear.
/// </summary>
public class RacingSchedulerProxy : DispatchProxy
{
    public int ScheduleCalls { get; private set; }

    public static IScheduler Create(out RacingSchedulerProxy state)
    {
        var scheduler = Create<IScheduler, RacingSchedulerProxy>();
        state = (RacingSchedulerProxy)(object)scheduler;
        return scheduler;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod!.Name)
        {
            case nameof(IScheduler.RescheduleJob):
                return Task.FromResult<DateTimeOffset?>(null);
            case nameof(IScheduler.ScheduleJob):
                ScheduleCalls++;
                return Task.FromException<DateTimeOffset>(new ObjectAlreadyExistsException("Trigger already exists."));
            default:
                throw new NotSupportedException(targetMethod.Name);
        }
    }
}

internal sealed class FixedSchedulerFactory(IScheduler scheduler) : ISchedulerFactory
{
    public Task<IReadOnlyList<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IScheduler>>([scheduler]);

    public Task<IScheduler> GetScheduler(CancellationToken cancellationToken = default) => Task.FromResult(scheduler);

    public Task<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IScheduler?>(scheduler);
}

/// <summary><c>IJobExecutionContext</c> falso: solo lo que lee el job de disparo.</summary>
public class FireContextProxy : DispatchProxy
{
    private JobDataMap _data = [];
    private int _refireCount;

    public static IJobExecutionContext Create(Guid tenantId, Guid reminderId, int refireCount)
    {
        var context = Create<IJobExecutionContext, FireContextProxy>();
        var state = (FireContextProxy)(object)context;
        state._refireCount = refireCount;
        state._data = new JobDataMap { ["tenantId"] = tenantId.ToString(), ["reminderId"] = reminderId.ToString() };
        return context;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod!.Name switch
        {
            "get_MergedJobDataMap" => _data,
            "get_RefireCount" => _refireCount,
            "get_CancellationToken" => CancellationToken.None,
            _ => throw new NotSupportedException(targetMethod.Name),
        };
}
