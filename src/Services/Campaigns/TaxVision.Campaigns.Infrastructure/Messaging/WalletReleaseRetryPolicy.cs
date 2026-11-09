using BuildingBlocks.Messaging;
using JasperFx;
using JasperFx.CodeGeneration;
using TaxVision.Campaigns.Application.Runs.Commands;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace TaxVision.Campaigns.Infrastructure.Messaging;

/// <summary>Esperas entre reintentos de liberar la reserva de un run rechazado (<c>Campaigns:WalletRelease</c>).</summary>
public sealed class WalletReleaseRetryOptions
{
    public const string SectionName = "Campaigns:WalletRelease";

    public static IReadOnlyList<TimeSpan> DefaultDelays { get; } =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
    ];

    /// <summary>Nulo usa <see cref="DefaultDelays"/>. Sin valor inicial: el binder concatena sobre arrays con elementos.</summary>
    public TimeSpan[]? Delays { get; set; }

    internal TimeSpan[] ResolveDelays()
    {
        var delays = Delays ?? [.. DefaultDelays];
        if (delays.Length == 0 || delays.Any(d => d <= TimeSpan.Zero))
            throw new ArgumentException(
                "Wallet release delays must contain at least one positive value.",
                nameof(Delays)
            );
        return delays;
    }
}

/// <summary>
/// El rechazo suele venir de un Wallet caído: los 21 s de la política estándar no alcanzan. Al agotarse, va a la cola
/// de errores. La espera vive en la base y sobrevive a un reinicio.
/// </summary>
public sealed class WalletReleaseRetryPolicy(WalletReleaseRetryOptions options) : IHandlerPolicy
{
    private readonly TimeSpan[] _delays = options.ResolveDelays();

    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains.Where(c => c.MessageType == typeof(ReleaseCampaignRunReservationCommand)))
        {
            // Las reglas de la cadena se evalúan antes que las globales: se repiten las permanentes.
            foreach (var permanent in WolverineFailurePolicies.PermanentFailureTypes)
                chain.OnExceptionOfType(permanent).MoveToErrorQueue();

            chain.OnException<Exception>().ScheduleRetry(_delays).Then.MoveToErrorQueue();
        }
    }
}
