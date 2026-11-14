using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Processing;
using TaxVision.FinancialInsights.Domain.Imports;
using Wolverine;

namespace TaxVision.FinancialInsights.Infrastructure.Processing;

/// <summary>
/// Toma un documento listo por vuelta (toma atómica), hace OCR e inferencia fuera de la transacción y registra el
/// resultado con un comando de Wolverine: una unidad por transacción, con outbox para el aviso de cierre del lote.
/// Nunca registra URLs firmadas ni el contenido del documento.
/// </summary>
public sealed class FinancialOcrWorker(
    IServiceScopeFactory scopes,
    IOptions<FinancialProcessingOptions> settings,
    IOptions<OpenAiFinancialOptions> openAi,
    ILogger<FinancialOcrWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan InterruptedAfter = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PlanRetryAfter = TimeSpan.FromMinutes(5);

    // Último intento de traer el plan por tenant: evita martillar Subscription si aún no tiene foto.
    private readonly Dictionary<Guid, DateTime> _planAttempts = [];

    // Estos códigos dejan el documento para revisión humana; el resto es un fallo técnico.
    private static readonly HashSet<string> HoldCodes =
    [
        "VisionPageLimit",
        "DocumentLimit",
        "NoReadableContent",
        "AccessChanged",
        "TenantAiLimit",
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.Enabled || string.IsNullOrWhiteSpace(openAi.Value.ApiKey))
        {
            logger.LogInformation("Financial OCR worker is idle: processing is disabled or the OpenAI key is absent.");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverInterruptedAsync(stoppingToken);
                await HealWaitingPlansAsync(stoppingToken);
                await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // El mensaje de una excepción HTTP de storage puede traer una URL firmada: solo el tipo.
                logger.LogWarning("Financial OCR loop failed with {ExceptionType}.", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.Value.PollSeconds, 2, 300)), stoppingToken);
        }
    }

    private async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        IReadOnlyList<ClaimCandidate> stale;
        await using (var scope = scopes.CreateAsyncScope())
            stale = await scope.ServiceProvider.GetRequiredService<ImportProcessingQueue>()
                .ListInterruptedAsync(DateTime.UtcNow - InterruptedAfter, 20, ct);

        foreach (var document in stale)
            await StopAsync(document, ImportDocumentStop.Fail, "Interrupted", ct);
    }

    // Documentos listos de un tenant sin fila de plan: se trae el plan por M2M en vez de dejarlos esperando.
    private async Task HealWaitingPlansAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> waiting;
        await using (var scope = scopes.CreateAsyncScope())
            waiting = await scope.ServiceProvider.GetRequiredService<ImportProcessingQueue>()
                .ListTenantsWaitingForPlanAsync(5, ct);

        var now = DateTime.UtcNow;
        foreach (var tenantId in waiting)
        {
            if (_planAttempts.TryGetValue(tenantId, out var last) && now - last < PlanRetryAfter)
                continue;
            _planAttempts[tenantId] = now;
            await using var scope = scopes.CreateAsyncScope();
            var plan = await scope.ServiceProvider.GetRequiredService<TenantEntitlementsResolver>().GetOrPullAsync(tenantId, ct);
            if (plan is null)
                logger.LogWarning("Entitlements for tenant {TenantId} are still unavailable; documents keep waiting.", tenantId);
        }
    }

    private async Task ProcessNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<ImportProcessingQueue>();
        foreach (var candidate in await queue.ListClaimCandidatesAsync(10, ct))
        {
            if (!await queue.TryClaimAsync(candidate, ct))
                continue;
            await ProcessClaimedAsync(candidate, scope.ServiceProvider, ct);
            return; // Un documento por vuelta acota el costo de OCR y del modelo.
        }
    }

    private async Task ProcessClaimedAsync(ClaimCandidate claimed, IServiceProvider services, CancellationToken ct)
    {
        var queue = services.GetRequiredService<ImportProcessingQueue>();
        var context = await queue.LoadContextAsync(claimed.TenantId, claimed.DocumentId, ct);
        if (context is null)
            return;

        // Carrera rara: la fila del plan falta justo al tomar el documento; se intenta traer antes de devolverlo.
        var plan = context.Plan ?? await services.GetRequiredService<TenantEntitlementsResolver>().GetOrPullAsync(claimed.TenantId, ct);
        context = context with { Plan = plan };
        switch (DocumentProcessingAccess.Evaluate(context.Tenant, context.Customer, context.Plan))
        {
            case DocumentAccess.Pending:
                await StopAsync(claimed, ImportDocumentStop.ReturnToQueue, "EntitlementsPending", ct);
                return;
            case DocumentAccess.Revoked:
                await StopAsync(claimed, ImportDocumentStop.HoldForReview, "AccessChanged", ct);
                return;
        }

        try
        {
            var file = await services.GetRequiredService<CloudStorageStatementSource>()
                .DownloadAsync(claimed.TenantId, context.CustomerId, context.FileId, ct);
            var pages = await services.GetRequiredService<LocalStatementExtractor>()
                .ExtractAsync(file.Bytes, file.MediaType, ct);
            var limit = context.Plan!.MonthlyDocumentLimit(settings.Value.MaxDocumentsPerTenantMonth);
            if (!await queue.TryReserveAsync(claimed.TenantId, claimed.DocumentId, openAi.Value.Model, limit, ct))
                throw new DocumentProcessingException("TenantAiLimit", "Tenant monthly document quota is exhausted.");

            var inference = await services.GetRequiredService<OpenAiStatementClient>().AnalyzeAsync(pages, ct);
            var outcome = await InvokeAsync(
                new CompleteImportDocumentCommand(
                    claimed.TenantId,
                    claimed.DocumentId,
                    inference
                        .Movements.Select(m => new SuggestedMovementInput(
                            m.TransactionDate,
                            m.Description,
                            m.Amount,
                            m.Currency,
                            m.Direction,
                            m.SuggestedCategory,
                            m.SourcePage
                        ))
                        .ToArray(),
                    inference.NeedsReview,
                    pages.Count,
                    inference.InputTokens,
                    inference.OutputTokens
                ),
                ct
            );
            if (outcome.IsSuccess)
                logger.LogInformation(
                    "Financial document {DocumentId} of tenant {TenantId} finished as {Status}.",
                    claimed.DocumentId,
                    claimed.TenantId,
                    outcome.Value
                );
            else
                LogRejected(claimed, outcome.Error);
        }
        catch (DocumentProcessingException ex)
        {
            var stop = HoldCodes.Contains(ex.Code) ? ImportDocumentStop.HoldForReview : ImportDocumentStop.Fail;
            logger.LogWarning(
                "Financial document {DocumentId} of tenant {TenantId} stopped: {Code}.",
                claimed.DocumentId,
                claimed.TenantId,
                ex.Code
            );
            await StopAsync(claimed, stop, ex.Code, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Financial document {DocumentId} of tenant {TenantId} failed with {ExceptionType}.",
                claimed.DocumentId,
                claimed.TenantId,
                ex.GetType().Name
            );
            await StopAsync(claimed, ImportDocumentStop.Fail, "ProcessingError", ct);
        }
    }

    private async Task StopAsync(ClaimCandidate document, ImportDocumentStop stop, string reason, CancellationToken ct)
    {
        var result = await InvokeAsync(new StopImportDocumentCommand(document.TenantId, document.DocumentId, stop, reason), ct);
        if (result.IsFailure)
            LogRejected(document, result.Error);
    }

    // Cada comando en su propio scope: su transacción y su outbox.
    private async Task<Result<ImportDocumentStatus>> InvokeAsync(object command, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        return await bus.InvokeAsync<Result<ImportDocumentStatus>>(command, ct);
    }

    private void LogRejected(ClaimCandidate document, Error error) =>
        logger.LogWarning(
            "Financial document {DocumentId} of tenant {TenantId} kept its state: {Code}.",
            document.DocumentId,
            document.TenantId,
            error.Code
        );
}
