using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace TaxVision.Billing.Infrastructure.Persistence;

/// <summary>
/// DbContext del servicio Billing. Igual que el resto de la plataforma, persistirá el estado del
/// agregado ANTES de drenar y publicar sus domain events al outbox durable de Wolverine, todo
/// dentro de la transacción ambiental de Wolverine (ver Program.cs).
///
/// SCAFFOLD B1: intencionalmente sin DbSets de dominio ni configuraciones EF todavía. El modelo
/// (owned types de los snapshots/Money, índices, RowVersion) y la migración inicial se agregan en
/// la fase B2 (ver documents/architecture/billing/{10_Billing_Data_Model,15_Billing_Implementation_Plan}.md).
/// Se mantiene design-time-constructible (dotnet ef) con un modelo vacío.
/// </summary>
public sealed class BillingDbContext(
    DbContextOptions<BillingDbContext> options,
    ITenantContext tenantContext,
    IMessageBus? messageBus = null
) : DbContext(options), IUnitOfWork
{
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly IMessageBus? _messageBus = messageBus;

    // B2: DbSet<Invoice> Invoices, DbSet<InvoiceLineItem>, DbSet<InvoicePaymentLink>,
    //     DbSet<PaymentReceipt> PaymentReceipts, DbSet<TenantBillingSettings>,
    //     DbSet<InvoiceNumberSequence> InvoiceNumberSequences + sus IEntityTypeConfiguration.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Persistir estado ANTES de despachar domain events, y solo después publicarlos. Wolverine
        // corre este SaveChanges dentro de su transacción ambiental, así que los eventos publicados
        // acá se encolan en el outbox durable y se entregan de forma atómica al commitear. El
        // drenado real de AggregateRoot.DomainEvents se implementa en B2/B3.
        var affected = await base.SaveChangesAsync(cancellationToken);
        return affected;
    }
}
