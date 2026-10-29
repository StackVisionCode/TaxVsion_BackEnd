using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Common;

/// <summary>
/// Mensajes que un handler junta mientras muta y publica solo después de guardar (T-8): si el
/// <c>SaveChanges</c> choca, Wolverine reintenta el handler en línea y lo ya publicado saldría igual
/// aunque la transacción se revirtió (avisos o comandos duplicados).
/// </summary>
public sealed class DeferredMessages
{
    private readonly List<object> _messages = [];

    public int Count => _messages.Count;

    public IReadOnlyList<object> Messages => _messages;

    public void Add(object message) => _messages.Add(message);

    public async Task PublishAsync(IMessageBus bus)
    {
        foreach (var message in _messages)
            await bus.PublishAsync(message);
        _messages.Clear();
    }
}
