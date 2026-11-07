namespace TaxVision.BuildingBlocks.Testing.Messaging;

/// <summary>BB-17. Lo que llegó a un handler de prueba por una cola local durable. Un sobre en la tabla de salida no dice si salió.</summary>
public sealed class DeliveredProbe
{
    private readonly List<object> _messages = [];

    public IReadOnlyList<object> All
    {
        get
        {
            lock (_messages)
                return [.. _messages];
        }
    }

    public IReadOnlyList<T> Of<T>() => All.OfType<T>().ToList();

    public void Add(object message)
    {
        lock (_messages)
            _messages.Add(message);
    }

    public void Clear()
    {
        lock (_messages)
            _messages.Clear();
    }

    /// <summary>Espera lo esperado y un momento más: un mensaje de más no llega al instante.</summary>
    public async Task SettleAsync(int expected, int seconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && All.Count < expected)
            await Task.Delay(100);
        await Task.Delay(500);
    }
}

/// <summary>BB-17. Armado, el handler real falla después de publicar y antes de confirmar.</summary>
public sealed class HandlerFailureSwitch
{
    public bool Armed { get; set; }
}

/// <summary>Se aplica a la cadena del handler real. Por InvokeAsync pasa por los 3 reintentos en línea: unos 21 s.</summary>
public static class FailAfterHandlerMiddleware
{
    public static void After(HandlerFailureSwitch failure)
    {
        if (failure.Armed)
            throw new ArgumentException("Failure after publishing (test).");
    }
}
