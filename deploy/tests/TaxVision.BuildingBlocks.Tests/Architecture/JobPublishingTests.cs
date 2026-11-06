using TaxVision.BuildingBlocks.Testing.Architecture;
using Xunit;
using Xunit.Abstractions;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// BB-3 sobre todos los servicios: ningún job nuevo publica por un <c>IMessageBus</c> suelto. La deuda de
/// hoy queda listada con el ítem del plan que la corrige; al adoptar, la entrada se retira (si no, falla
/// <see cref="JobPublishingRulesBase.PendingAdoption_EntriesAreStillNeeded"/>).
///
/// <para><b>Aceptado sin migrar:</b> "<c>new MessageBus(runtime)</c> (o el bus del scope) + <c>InvokeAsync</c> por
/// entidad" (CloudStorage <c>StuckScanRescueService</c>, Notes <c>PendingAttachmentReconciliationJob</c>, Wallet,
/// Campaigns, PaymentClient y otros). Cada invocación corre el handler en el pipeline de Wolverine con su
/// scope, su transacción y su outbox: ya es una unidad por entidad. Ver <see cref="JobPublishingRulesBase"/>.</para>
///
/// <para><b>Excepciones permanentes:</b> Auth <c>PermissionsReconciliationService</c> (solo re-publica, no guarda)
/// y Scribe <c>CloudStorageClient</c> (no es un job; lo confirma la T-INT de SCR-07).</para>
/// </summary>
public sealed class JobPublishingTests(ITestOutputHelper output) : JobPublishingRulesBase
{
    protected override string SourceDirectory => "src/Services";

    protected override IReadOnlyDictionary<string, string> PendingAdoption { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
        };

    protected override IReadOnlyDictionary<string, string> JustifiedExceptions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Solo invoca los handlers transaccionales de reconciliación, cada uno con su outbox. No publica por su cuenta.
            ["src/Services/Postmaster/TaxVision.Postmaster.Api/Jobs/DeliveryFeedbackReconciliationJob.cs"] =
                "PMS-08: solo InvokeAsync de handlers transaccionales; el poll sale al job con PMS-06",
            // Solo re-publica (anti-entropy cada 6 h) y no guarda nada: lo que no salga lo repite el pase siguiente.
            ["src/Services/Auth/Api/Jobs/PermissionsReconciliationService.cs"] = "AUTH-BB3: solo publica",
            // No es un job: SaveFileRequested sale antes del commit a propósito (T-INT de SCR-07).
            ["src/Services/Scribe/TaxVision.Scribe.Infrastructure/Storage/CloudStorageClient.cs"] =
                "SCR-07: el archivo debe existir antes de que una fila lo apunte; peor caso: blob huérfano",
            // Solo re-publican y no guardan. Reminder deduplica por RequestKey y el pedido de sala se repite la vuelta siguiente.
            ["src/Services/Calendar/TaxVision.Calendar.Infrastructure/Jobs/ReminderScheduleJob.cs"] = "CAL-BB3: solo publica",
            ["src/Services/Calendar/TaxVision.Calendar.Infrastructure/Jobs/MeetingLinkReconciliationJob.cs"] =
                "CAL-BB3: solo publica",
            // Le pasa el bus a SmsReconciliation.RunAsync, que solo hace InvokeAsync de handlers transaccionales.
            ["src/Services/Sms/TaxVision.Sms.Infrastructure/Jobs/SmsStatusReconciliationService.cs"] =
                "SMS-11: solo InvokeAsync",
        };

    [Fact]
    public void Inventory_IsReported()
    {
        var findings = JobPublishingRules.Scan(SourceDirectory);
        Assert.NotEmpty(findings);

        foreach (var group in findings.GroupBy(f => f.Kind).OrderBy(g => g.Key))
        {
            output.WriteLine($"{group.Key}: {group.Count()}");
            foreach (var finding in group.OrderBy(f => f.Path, StringComparer.Ordinal))
                output.WriteLine($"  {finding.Path}");
        }
    }
}

/// <summary>BB-3. El clasificador de <see cref="JobPublishingRules"/> con fuentes de ejemplo.</summary>
public sealed class JobPublishingRulesTests
{
    [Fact]
    public void ScopedBus_PublishingAfterSave_IsAViolation() =>
        Assert.Equal(
            JobPublishingKind.PublishesThroughLooseBus,
            JobPublishingRules.Classify(
                """
                public sealed class SweepJob(IServiceScopeFactory scopes) : BackgroundService
                {
                    async Task RunAsync()
                    {
                        using var scope = scopes.CreateScope();
                        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                        await unitOfWork.SaveChangesAsync(ct);
                        await bus.PublishAsync(new Expired());
                    }
                }
                """
            )
        );

    [Fact]
    public void BusHandedToAPublishHelper_IsAViolation() =>
        Assert.Equal(
            JobPublishingKind.PublishesThroughLooseBus,
            JobPublishingRules.Classify(
                """
                public sealed class BackfillJob(IServiceScopeFactory scopes) : PeriodicJob
                {
                    async Task RunAsync(IServiceProvider services)
                    {
                        var bus = services.GetRequiredService<IMessageBus>();
                        await ReceiptPublisher.PublishReceiptAsync(payment, bus, ct);
                    }
                }
                """
            )
        );

    [Fact]
    public void InlineResolvedBusHandedToASendHelper_IsAViolation() =>
        Assert.Equal(
            JobPublishingKind.PublishesThroughLooseBus,
            JobPublishingRules.Classify(
                """
                public sealed class ReminderJob(IServiceScopeFactory scopes) : BackgroundService
                {
                    Task RunAsync(IServiceProvider services) =>
                        Reminders.SendIfDueAsync(candidate, services.GetRequiredService<IUnitOfWork>(), services.GetRequiredService<IMessageBus>(), ct);
                }
                """
            )
        );

    [Fact]
    public void BusHandedToAnotherMethod_IsReportedForManualReview() =>
        Assert.Equal(
            JobPublishingKind.BusHandedOff,
            JobPublishingRules.Classify(
                """
                public sealed class StatusJob(IServiceScopeFactory scopes) : BackgroundService
                {
                    async Task RunAsync(IServiceProvider services)
                    {
                        var bus = services.GetRequiredService<IMessageBus>();
                        await Reconciliation.RunAsync(bus, command, ct);
                    }
                }
                """
            )
        );

    [Fact]
    public void OutboxOfTheUnit_IsFine() =>
        Assert.Equal(
            JobPublishingKind.NoLooseBus,
            JobPublishingRules.Classify(
                """
                internal sealed class OverdueJob(IServiceScopeFactory scopes) : BackgroundService
                {
                    async Task RunAsync()
                    {
                        var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<TasksDbContext>>();
                        await outbox.PublishAsync(evt);
                        await outbox.SaveChangesAndFlushMessagesAsync(ct);
                    }
                }
                """
            )
        );

    [Fact]
    public void OwnBus_InvokePerEntity_IsAccepted() =>
        Assert.Equal(
            JobPublishingKind.InvokesPerEntity,
            JobPublishingRules.Classify(
                """
                public sealed class RescueService(IWolverineRuntime runtime) : BackgroundService
                {
                    async Task RunAsync()
                    {
                        var bus = new MessageBus(runtime);
                        foreach (var file in stuck)
                            await bus.InvokeAsync(new RescueCommand(file.Id), ct);
                    }
                }
                """
            )
        );

    [Fact]
    public void ScopedBus_InvokePerEntity_IsAccepted() =>
        Assert.Equal(
            JobPublishingKind.InvokesPerEntity,
            JobPublishingRules.Classify(
                """
                public sealed class ExpireJob(IServiceScopeFactory scopes) : BackgroundService
                {
                    public static async Task ExpireAsync(IMessageBus bus) =>
                        await bus.InvokeAsync<Result>(new ExpireCommand(), ct);
                }
                """
            )
        );

    [Fact]
    public void NonJobClass_IsIgnored() =>
        Assert.Null(
            JobPublishingRules.Classify(
                """
                public sealed class StorageClient(IMessageBus bus)
                {
                    Task UploadAsync() => bus.PublishAsync(new SaveFileRequested()).AsTask();
                }
                """
            )
        );

    [Fact]
    public void Comments_AreIgnored() =>
        Assert.Equal(
            JobPublishingKind.InvokesPerEntity,
            JobPublishingRules.Classify(
                """
                public sealed class ReconcileJob : BackgroundService
                {
                    // Antes: bus.PublishAsync(evt) después de guardar.
                    async Task RunAsync(IMessageBus bus) => await bus.InvokeAsync(new Reconcile());
                }
                """
            )
        );
}
