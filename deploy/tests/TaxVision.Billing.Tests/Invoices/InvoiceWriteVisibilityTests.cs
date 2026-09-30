using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.DeleteInvoice;
using TaxVision.Billing.Domain.Invoices;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// A1 — la visibilidad por asignación ya filtraba las lecturas de facturas, pero emitir, editar,
/// anular, borrar y registrar un pago entraban con solo tener <c>invoicing.manage</c>. Lo que se
/// comprueba acá es la decisión del handler: con qué <c>assignedTo</c> llama al repositorio, que es
/// donde vive el filtro (ya probado por el camino de lectura).
/// </summary>
public sealed class InvoiceWriteVisibilityTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    /// <summary>Anota con qué alcance se pidió la factura y responde que no existe: basta para la decisión.</summary>
    private sealed class RecordingInvoices : IInvoiceRepository
    {
        public Guid? AskedFor { get; private set; }
        public bool WasAsked { get; private set; }

        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        )
        {
            WasAsked = true;
            AskedFor = assignedToUserId;
            return Task.FromResult<Invoice?>(null);
        }

        public Task<Invoice?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Invoice>> ListByTenantAsync(
            Guid tenantId,
            int take,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        ) => throw new NotSupportedException();

        public Task AddAsync(Invoice invoice, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private static async Task<Guid?> ScopeUsedAsync(bool visibilityEnabled, bool canViewAll)
    {
        var invoices = new RecordingInvoices();
        await DeleteInvoiceHandler.Handle(
            new DeleteInvoiceCommand(Tenant, Guid.NewGuid(), Actor, canViewAll),
            invoices,
            Options.Create(new BillingVisibilityOptions { Enabled = visibilityEnabled }),
            new NoOpUnitOfWork(),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.True(invoices.WasAsked);
        return invoices.AskedFor;
    }

    [Fact]
    public async Task A_preparer_only_reaches_the_invoices_of_his_own_customers()
    {
        Assert.Equal(Actor, await ScopeUsedAsync(visibilityEnabled: true, canViewAll: false));
    }

    [Fact]
    public async Task View_all_lifts_the_scope()
    {
        Assert.Null(await ScopeUsedAsync(visibilityEnabled: true, canViewAll: true));
    }

    [Fact]
    public async Task With_the_flag_off_nothing_changes()
    {
        Assert.Null(await ScopeUsedAsync(visibilityEnabled: false, canViewAll: false));
    }

    [Fact]
    public void Every_invoice_write_handler_scopes_by_assignment()
    {
        string[] writes = ["IssueInvoice", "RecordManualPayment", "EditInvoice", "DeleteInvoice", "VoidInvoice"];

        var root = RepositoryRoot();
        var missing = writes
            .Where(write =>
            {
                var directory = Path.Combine(
                    root,
                    "src",
                    "Services",
                    "Billing",
                    "TaxVision.Billing.Application",
                    "Invoices",
                    write
                );
                return !Directory
                    .GetFiles(directory, "*.cs")
                    .Any(file => File.ReadAllText(file).Contains("assignedTo", StringComparison.Ordinal));
            })
            .ToList();

        Assert.True(missing.Count == 0, "Escrituras sin alcance por asignación: " + string.Join(", ", missing));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
