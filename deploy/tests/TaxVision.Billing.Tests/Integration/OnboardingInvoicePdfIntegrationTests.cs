using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-12 con SQL real y los consumidores reales: en el orden normal el PDF se pide bajo la plataforma, y el
/// re-hospedaje puede ganarle. Base temporal propia, que se borra al arrancar y al terminar.
/// </summary>
public sealed class OnboardingInvoicePdfIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "billing_bil12_pdf_it";

    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SqlConnectionStringBuilder Server = new(BillingTestDatabase.LocalConnectionString);

    private static readonly string Connection = new SqlConnectionStringBuilder(Server.ConnectionString)
    {
        InitialCatalog = DatabaseName,
    }.ConnectionString;

    private readonly Guid _onboardingId = Guid.NewGuid();
    private readonly Guid _realTenantId = Guid.NewGuid();
    private readonly RecordingDocuments _documents = new();

    public async Task InitializeAsync()
    {
        await DropDatabaseAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await DropDatabaseAsync();
    }

    // El re-hospedaje confirma antes de que corra el comando del PDF.
    [Fact]
    public async Task RehomeBeforeTheCommand_PdfIsStillRequested()
    {
        var command = await InvoiceRequestedAsync();
        await TenantCreatedAsync();

        await GeneratePdfAsync(command);

        Assert.Equal(_realTenantId, Assert.Single(_documents.TenantIds));
    }

    // Documents ya generó bajo la plataforma, y el aviso llega después del re-hospedaje.
    [Fact]
    public async Task RehomeBeforeTheCompletion_PdfIsAttached()
    {
        var command = await InvoiceRequestedAsync();
        await GeneratePdfAsync(command);
        await TenantCreatedAsync();
        var fileId = Guid.NewGuid();

        await PdfCompletedAsync(PlatformTenant.Id, command.InvoiceId, fileId);

        await using var db = NewContext();
        var invoice = await db.Invoices.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(_realTenantId, invoice.TenantId);
        Assert.Equal(fileId, invoice.PdfFileId);
    }

    // El orden sin carrera sigue igual.
    [Fact]
    public async Task PdfBeforeTheRehome_IsAttachedUnderThePlatform()
    {
        var command = await InvoiceRequestedAsync();
        await GeneratePdfAsync(command);
        var fileId = Guid.NewGuid();

        await PdfCompletedAsync(PlatformTenant.Id, command.InvoiceId, fileId);

        await using var db = NewContext();
        Assert.Equal(fileId, (await db.Invoices.IgnoreQueryFilters().SingleAsync()).PdfFileId);
        Assert.Equal(PlatformTenant.Id, Assert.Single(_documents.TenantIds));
    }

    // Una factura normal de otro tenant no se alcanza desde la plataforma.
    [Fact]
    public async Task OrdinaryInvoiceOfAnotherTenant_IsNotReachedFromThePlatform()
    {
        var command = await InvoiceRequestedAsync();
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE billing.Invoices SET OnboardingId = NULL, TenantId = {_realTenantId} WHERE Id = {command.InvoiceId}"
            );
        }

        await GeneratePdfAsync(command);
        await PdfCompletedAsync(PlatformTenant.Id, command.InvoiceId, Guid.NewGuid());

        await using var check = NewContext();
        Assert.Empty(_documents.TenantIds);
        Assert.Null((await check.Invoices.IgnoreQueryFilters().SingleAsync()).PdfFileId);
    }

    private async Task<GenerateInvoicePdfCommand> InvoiceRequestedAsync()
    {
        await using var db = NewContext();
        var bus = new FakeMessageBus();
        await OnboardingInvoiceRequestedConsumer.Handle(
            new OnboardingInvoiceRequestedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = _onboardingId,
                PlanId = Guid.NewGuid(),
                PlanDescription = "Enterprise (Monthly)",
                PayerName = "Owner",
                PaymentId = Guid.NewGuid(),
                GrossAmountCents = 4900,
                DiscountAmountCents = 0,
                NetAmountCents = 4900,
                Currency = "USD",
                SettlementType = "Paid",
            },
            new InvoiceRepository(db),
            new InvoiceNumberSequenceRepository(db),
            new OnboardingTenantMapRepository(db),
            db,
            bus,
            Options.Create(new PlatformIssuerOptions()),
            new PlatformStub(),
            new FixedClock(Now),
            new NoopCorrelationContext(),
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );
        return bus.LastOfType<GenerateInvoicePdfCommand>()!;
    }

    private async Task TenantCreatedAsync()
    {
        await using var db = NewContext();
        await OnboardingInvoiceBackfillConsumer.Handle(
            new TenantCreatedForOnboardingIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = _onboardingId,
                CreatedTenantId = _realTenantId,
            },
            new InvoiceRepository(db),
            new OnboardingTenantMapRepository(db),
            db,
            new FixedClock(Now),
            new NoopCorrelationContext(),
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );
    }

    private async Task GeneratePdfAsync(GenerateInvoicePdfCommand command)
    {
        await using var db = NewContext();
        await GenerateInvoicePdfHandler.Handle(
            command,
            new InvoiceRepository(db),
            _documents,
            new NoopCorrelationContext(),
            CancellationToken.None
        );
    }

    private static async Task PdfCompletedAsync(Guid tenantId, Guid invoiceId, Guid fileId)
    {
        await using var db = NewContext();
        await DocumentGenerationCompletedConsumer.Handle(
            new DocumentGenerationCompletedIntegrationEvent
            {
                TenantId = tenantId,
                GenerationId = Guid.NewGuid(),
                DocumentType = "Invoice",
                OwnerType = "Invoice",
                OwnerId = invoiceId,
                DocumentVersion = 1,
                FileId = fileId,
                FileName = "invoice.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024,
            },
            new InvoiceRepository(db),
            db,
            new NoopCorrelationContext(),
            new FixedClock(Now),
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );
    }

    private static BillingDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(Connection).Options, new AnyTenant());

    private static async Task DropDatabaseAsync()
    {
        var master = new SqlConnectionStringBuilder(Server.ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RecordingDocuments : IInvoiceDocumentClient
    {
        public List<Guid> TenantIds { get; } = [];

        public Task<Result> GenerateAsync(
            InvoiceDocumentRequest request,
            Guid tenantId,
            string idempotencyKey,
            string? correlationId,
            CancellationToken ct = default
        )
        {
            TenantIds.Add(tenantId);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class AnyTenant : ITenantContext
    {
        public Guid TenantId => PlatformTenant.Id;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class PlatformStub : IPlatformSettings
    {
        public PlatformSettingsSnapshot Current { get; } =
            new(1, "Brand", "Brand LLC", "brand.test", "api.brand.test", "help@brand.test", "billing@brand.test");
    }
}
