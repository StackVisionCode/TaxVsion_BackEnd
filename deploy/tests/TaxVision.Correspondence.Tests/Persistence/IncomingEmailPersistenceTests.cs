using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;

namespace TaxVision.Correspondence.Tests.Persistence;

/// <summary>
/// Verifica que el mapeo EF de <see cref="IncomingEmail"/> (child tables reales para
/// Recipients/Attachments, backing fields) persiste y recarga correctamente.
///
/// <para>
/// El dedup por <c>(TenantId, InternetMessageId)</c> es un unique filtered index de SQL
/// Server (ver <c>IncomingEmailConfiguration</c>) — el proveedor InMemory de EF Core no
/// aplica constraints únicos, así que no es posible probarlo acá; queda enforced solo por
/// la migration generada (<c>AddInboxAggregates</c>), que sí se verificó manualmente.
/// </para>
/// </summary>
public sealed class IncomingEmailPersistenceTests
{
    // RBAC Fase 5 — EmailThread/IncomingEmail ahora son ITenantOwned; se setea el tenant "propio"
    // del test antes de consultar, igual que haría JwtTenantContextMiddleware en producción.
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static CorrespondenceDbContext CreateContext(string databaseName, ITenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static IncomingEmail NewIncomingEmail(Guid tenantId, Guid customerId, Guid emailThreadId) =>
        IncomingEmail
            .Create(
                tenantId,
                customerId,
                emailThreadId,
                Guid.NewGuid(),
                "gmail",
                "provider-msg-1",
                EmailAddress.Create("customer@example.com").Value,
                "The Customer",
                "Subject",
                "Snippet",
                DateTime.UtcNow,
                hasAttachments: true,
                attachmentCount: 1,
                recipients:
                [
                    new IncomingEmailRecipientData(
                        EmailAddress.Create("tenant-user@example.com").Value,
                        EmailRecipientType.To,
                        "Tenant User"
                    ),
                ],
                attachments:
                [
                    new IncomingEmailAttachmentData("invoice.pdf", "application/pdf", 2048, "provider-att-1", false),
                ]
            )
            .Value;

    [Fact]
    public async Task Recipients_and_attachments_persist_as_their_own_child_rows_and_reload_correctly()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        Guid threadId;

        await using (var db = CreateContext(databaseName, tenantContext))
        {
            var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
            threadId = thread.Id;
            var email = NewIncomingEmail(tenantId, customerId, thread.Id);

            db.EmailThreads.Add(thread);
            db.IncomingEmails.Add(email);
            await db.SaveChangesAsync();

            // Verifica que las filas hijas quedaron en sus propias tablas, no en una columna JSON.
            Assert.Equal(1, await db.IncomingEmailRecipients.CountAsync(r => r.IncomingEmailId == email.Id));
            Assert.Equal(1, await db.IncomingEmailAttachments.CountAsync(a => a.IncomingEmailId == email.Id));
        }

        await using var reloadDb = CreateContext(databaseName, tenantContext);
        var reloaded = await reloadDb
            .IncomingEmails.Include(x => x.Recipients)
            .Include(x => x.Attachments)
            .SingleAsync(x => x.CustomerId == customerId);

        Assert.Single(reloaded.Recipients);
        Assert.Single(reloaded.Attachments);
        Assert.Equal(customerId, reloaded.CustomerId);
        Assert.Equal(threadId, reloaded.EmailThreadId);
    }
}
