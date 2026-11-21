using System.Reflection;
using Wolverine;

namespace TaxVision.FinancialInsights.Tests.TestDoubles;

/// <summary>Bus falso: guarda lo publicado; cualquier otra llamada falla.</summary>
public class RecordingBus : DispatchProxy
{
    public List<object> Published { get; } = [];

    public static (IMessageBus Bus, RecordingBus Recorder) Create()
    {
        var bus = Create<IMessageBus, RecordingBus>();
        return (bus, (RecordingBus)(object)bus);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IMessageBus.PublishAsync))
            throw new NotSupportedException(targetMethod?.Name);
        Published.Add(args![0]!);
        return ValueTask.CompletedTask;
    }
}
