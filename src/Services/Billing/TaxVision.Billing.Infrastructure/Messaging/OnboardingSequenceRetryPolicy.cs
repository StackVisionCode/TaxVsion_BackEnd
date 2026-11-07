using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Results;
using JasperFx;
using JasperFx.CodeGeneration;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace TaxVision.Billing.Infrastructure.Messaging;

/// <summary>Esperas del reintento de la secuencia de onboarding. Cada una se alarga al azar hasta el doble.</summary>
public sealed class OnboardingSequenceRetryOptions
{
    public const string SectionName = "Billing:OnboardingSequenceRetry";

    public static readonly TimeSpan[] DefaultDelays =
    [
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(700),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1.5),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(2.5),
        TimeSpan.FromSeconds(2.5),
    ];

    public TimeSpan[]? Delays { get; set; }
}

/// <summary>
/// BIL-11: todos los onboardings comparten una fila de secuencia, así que en una ráfaga chocan entre sí. En
/// memoria y con espera al azar: el reintento programado sale con el sondeo, todos a la vez, y vuelven a chocar.
/// El handler guarda antes de publicar, así que repetirlo en línea no filtra nada.
/// </summary>
public sealed class OnboardingSequenceRetryPolicy(OnboardingSequenceRetryOptions options) : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        var delays = options.Delays is { Length: > 0 } configured
            ? configured
            : OnboardingSequenceRetryOptions.DefaultDelays;

        foreach (var chain in chains.Where(c => c.MessageType == typeof(OnboardingInvoiceRequestedIntegrationEvent)))
        {
            // Reglas de la cadena antes que las globales. Cubre también el choque al crear la fila del año.
            chain.OnException<ConflictException>().RetryWithCooldown(delays).WithFullJitter().Then.MoveToErrorQueue();
        }
    }
}
