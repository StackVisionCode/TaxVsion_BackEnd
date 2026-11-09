using Wolverine;

namespace BuildingBlocks.Web.Hosting;

/// <summary>
/// BB-2. Todas las colas locales del servicio pasan a durables: lo que se manda con <c>PublishAsync</c> o
/// <c>SendAsync</c> a un handler propio queda guardado en <c>wolverine_incoming_envelopes</c> y sobrevive a un
/// reinicio. <c>UseDurableOutboxOnAllSendingEndpoints</c> no cubre las colas locales; por eso esta pieza aparte.
/// Opcional: solo cambia el servicio que la llama. Las colas que ya tienen <c>UseDurableInbox()</c> quedan igual.
/// </summary>
public static class DurableLocalQueueExtensions
{
    public static WolverineOptions UseTaxVisionDurableLocalQueues(this WolverineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Policies.UseDurableLocalQueues();
        return options;
    }
}
