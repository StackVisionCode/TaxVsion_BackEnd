using BuildingBlocks.Messaging.AuthIntegrationEvents;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.RabbitMQ;
using Wolverine.Runtime.Handlers;

namespace BuildingBlocks.Web.Hosting;

/// <summary>
/// Acuse por servicio del retiro (offboard): todo handler de <see cref="UserOffboardedIntegrationEvent"/>
/// publica <see cref="UserOffboardingStepCompletedIntegrationEvent"/> al terminar con éxito, sin tocar los
/// consumers. Lo activa <see cref="RabbitMqTransportExtensions.UseTaxVisionRabbitMq(WolverineOptions, Uri)"/>.
/// <para>
/// El acuse se agrega como primer postprocesador de la cadena: corre después de TODOS los handlers del
/// mensaje (modo clásico: una cadena con varios handlers en serie) y antes del SaveChanges/commit del
/// middleware transaccional, así que con outbox queda en la misma transacción que lo que hizo el handler.
/// Si un handler lanza, la cadena entera se reintenta y no sale ningún acuse; un acuse repetido (reintento
/// tras un fallo posterior) lo absorbe Auth, que es idempotente por Service+Step.
/// </para>
/// </summary>
public static class OffboardingStepAcknowledgements
{
    /// <summary>Registra la política y la ruta del acuse al fanout común.</summary>
    public static WolverineOptions AcknowledgeOffboardingSteps(this WolverineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Policies.Add(new OffboardingStepAcknowledgementPolicy());
        options.PublishMessage<UserOffboardingStepCompletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
        return options;
    }

    /// <summary>
    /// Servicio dueño de un handler: el segundo tramo del ensamblado (<c>TaxVision.Calendar.Application</c> →
    /// <c>Calendar</c>), que coincide con la carpeta de src/Services. Fuera de esa convención, el nombre del
    /// ensamblado completo.
    /// </summary>
    public static string ServiceOf(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        var name = handlerType.Assembly.GetName().Name ?? handlerType.Assembly.FullName ?? "unknown";
        var parts = name.Split('.');
        return parts.Length > 1 && parts[0] == "TaxVision" ? parts[1] : name;
    }
}

/// <summary>Un paso a acusar: servicio + clase del handler.</summary>
public sealed record OffboardingStep(string Service, string Step);

/// <summary>Lo que corre el código generado por la política (un acuse por handler de la cadena).</summary>
public static class OffboardingStepAcknowledger
{
    public static async Task PublishAsync(
        UserOffboardedIntegrationEvent message,
        IMessageContext context,
        OffboardingStep[] steps,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();

        var now = DateTime.UtcNow;
        foreach (var step in steps)
        {
            await context.PublishAsync(
                new UserOffboardingStepCompletedIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = message.CorrelationId,
                    UserId = message.UserId,
                    Service = step.Service,
                    Step = step.Step,
                    CompletedAtUtc = now,
                }
            );
        }
    }
}

/// <summary>Política de Wolverine: agrega el acuse a las cadenas de <see cref="UserOffboardedIntegrationEvent"/>.</summary>
public sealed class OffboardingStepAcknowledgementPolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains.Where(c => c.MessageType == typeof(UserOffboardedIntegrationEvent)))
        {
            AddTo(chain);
            // Handlers separados o "sticky": cada sub-cadena acusa los suyos.
            foreach (var byEndpoint in chain.ByEndpoint)
                AddTo(byEndpoint);
        }
    }

    private static void AddTo(HandlerChain chain)
    {
        var steps = chain
            .Handlers.Select(h => h.HandlerType)
            .Distinct()
            .Select(t => new OffboardingStep(OffboardingStepAcknowledgements.ServiceOf(t), t.Name))
            // Orden fijo: el código pregenerado tiene que salir igual en cada codegen.
            .OrderBy(step => step.Service, StringComparer.Ordinal)
            .ThenBy(step => step.Step, StringComparer.Ordinal)
            .ToArray();
        if (steps.Length == 0 || chain.Postprocessors.OfType<PublishOffboardingAcknowledgementsFrame>().Any())
            return;

        chain.Postprocessors.Insert(0, new PublishOffboardingAcknowledgementsFrame(steps));
    }
}

/// <summary>Frame generado: <c>await OffboardingStepAcknowledger.PublishAsync(msg, context, [...], ct);</c>.</summary>
internal sealed class PublishOffboardingAcknowledgementsFrame(OffboardingStep[] steps) : AsyncFrame
{
    private Variable? _message;
    private Variable? _context;
    private Variable? _cancellation;

    public IReadOnlyList<OffboardingStep> Steps => steps;

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        _message = chain.FindVariable(typeof(UserOffboardedIntegrationEvent));
        yield return _message;
        _context = chain.FindVariable(typeof(IMessageContext));
        yield return _context;
        _cancellation = chain.FindVariable(typeof(CancellationToken));
        yield return _cancellation;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        var stepType = typeof(OffboardingStep).FullNameInCode();
        var items = string.Join(", ", steps.Select(s => $"new {stepType}({Literal(s.Service)}, {Literal(s.Step)})"));
        writer.WriteComment("Acuse del retiro por handler (OffboardingStepAcknowledgementPolicy)");
        writer.Write(
            $"await {typeof(OffboardingStepAcknowledger).FullNameInCode()}.{nameof(OffboardingStepAcknowledger.PublishAsync)}"
                + $"({_message!.Usage}, {_context!.Usage}, new {stepType}[] {{ {items} }}, {_cancellation!.Usage})"
                + ".ConfigureAwait(false);"
        );
        Next?.GenerateCode(method, writer);
    }

    // Nombres de tipo y ensamblado: se escapan igual por si acaso.
    private static string Literal(string value) =>
        "\""
        + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
        + "\"";
}
