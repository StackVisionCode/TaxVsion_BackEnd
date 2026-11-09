using BuildingBlocks.Messaging;
using JasperFx;
using JasperFx.CodeGeneration;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace TaxVision.Billing.Infrastructure.Messaging;

/// <summary>
/// Reintentos largos (BIL-3b) solo para los pasos que dependen de otro servicio: si PaymentClient o
/// Inventory caen por un despliegue, el aviso espera hasta ~1 h 43 min en vez de ~21 s antes de ir a la
/// cola de errores. Los fallos permanentes siguen yendo directo a la cola de errores.
/// </summary>
public sealed class LongRetryHandlerPolicy(params Type[] messageTypes) : IHandlerPolicy
{
    public static readonly TimeSpan[] Cooldowns =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
    ];

    public IReadOnlyList<Type> MessageTypes => messageTypes;

    /// <summary>La política de Billing: enlace de cobro y sync de stock.</summary>
    public static LongRetryHandlerPolicy ForSyncCommands() =>
        new(typeof(EnsureInvoicePaymentLinkCommand), typeof(SyncInvoiceStockCommand));

    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains.Where(c => messageTypes.Contains(c.MessageType)))
        {
            // Las reglas de la cadena se evalúan antes que las globales: se repiten las permanentes.
            foreach (var permanent in WolverineFailurePolicies.PermanentFailureTypes)
                chain.OnExceptionOfType(permanent).MoveToErrorQueue();

            // ScheduleRetry deja el reintento guardado en la base (no ocupa al consumidor en memoria).
            chain.OnException<Exception>().ScheduleRetry(Cooldowns);
        }
    }
}
