using BuildingBlocks.Messaging;
using JasperFx;
using JasperFx.CodeGeneration;
using TaxVision.Sms.Application.Messages.Commands;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace TaxVision.Sms.Infrastructure.Messaging;

/// <summary>
/// Esperas entre reintentos de <see cref="SettleSmsBatchCommand"/>. Por defecto suman ~43 min, dentro de la hora
/// en que vence la reserva <c>individual-sms</c>: una liquidación después del vencimiento ya no cobra.
/// </summary>
public sealed class WalletSettleRetryOptions
{
    public const string SectionName = "Sms:WalletSettle";

    public static IReadOnlyList<TimeSpan> DefaultDelays { get; } =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
    ];

    /// <summary>Nulo usa <see cref="DefaultDelays"/>. Sin valor inicial: el binder concatena sobre arrays con elementos.</summary>
    public TimeSpan[]? Delays { get; set; }

    internal TimeSpan[] ResolveDelays()
    {
        var delays = Delays ?? [.. DefaultDelays];
        if (delays.Length == 0 || delays.Any(d => d <= TimeSpan.Zero))
            throw new ArgumentException(
                "Wallet settle delays must contain at least one positive value.",
                nameof(Delays)
            );
        return delays;
    }
}

/// <summary>
/// La liquidación espera al Wallet con reintentos largos: los 21 s de la política estándar no alcanzan para un
/// despliegue. Al agotarse, va a la cola de errores. La espera vive en la base y sobrevive a un reinicio.
/// </summary>
public sealed class WalletSettleRetryPolicy(WalletSettleRetryOptions options) : IHandlerPolicy
{
    private readonly TimeSpan[] _delays = options.ResolveDelays();

    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains.Where(c => c.MessageType == typeof(SettleSmsBatchCommand)))
        {
            // Las reglas de la cadena se evalúan antes que las globales: se repiten las permanentes.
            foreach (var permanent in WolverineFailurePolicies.PermanentFailureTypes)
                chain.OnExceptionOfType(permanent).MoveToErrorQueue();

            chain.OnException<Exception>().ScheduleRetry(_delays).Then.MoveToErrorQueue();
        }
    }
}
