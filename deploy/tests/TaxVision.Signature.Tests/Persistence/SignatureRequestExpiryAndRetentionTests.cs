using System.Reflection;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Signature.Tests.Persistence;

/// <summary>
/// F2 (expiración por envío + retención de borradores): sólo lo enviado (InProgress) expira por
/// reloj de firma; los borradores (Draft/Ready) se limpian por retención, nunca por expiración.
/// </summary>
public sealed class SignatureRequestExpiryAndRetentionTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task ListExpiredCandidatesAsync_solo_devuelve_InProgress_ya_vencidas()
    {
        var db = NewContext();
        var repo = new SignatureRequestRepository(db);
        var now = DateTime.UtcNow;

        var sentExpired = ReadyWithField(); // enviada hace 5 días con expiración de 72h → vencida
        sentExpired.Send(now.AddDays(-5));

        var sentActive = ReadyWithField();
        sentActive.Send(now); // enviada ahora → expira en el futuro

        var readyPastExpiry = ReadyWithField();
        Backdate(readyPastExpiry, expires: now.AddDays(-1)); // Ready con expiración pasada: NO cuenta

        var draftPastExpiry = Draft();
        Backdate(draftPastExpiry, expires: now.AddDays(-1)); // Draft con expiración pasada: NO cuenta

        await SeedAsync(db, sentExpired, sentActive, readyPastExpiry, draftPastExpiry);

        var candidates = await repo.ListExpiredCandidatesAsync(now);

        Assert.Equal(new[] { sentExpired.Id }, candidates.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task ListStaleUnsentAsync_devuelve_borradores_viejos_excluye_hold_recientes_y_enviadas()
    {
        var db = NewContext();
        var repo = new SignatureRequestRepository(db);
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-30);

        var oldDraft = Draft();
        Backdate(oldDraft, updated: now.AddDays(-40));

        var oldReady = ReadyWithField();
        Backdate(oldReady, updated: now.AddDays(-40));

        var freshDraft = Draft(); // tocado ahora → excluido

        var heldOldDraft = Draft();
        Backdate(heldOldDraft, updated: now.AddDays(-40), legalHold: true); // LegalHold → excluido

        var sentOld = ReadyWithField();
        sentOld.Send(now.AddDays(-40));
        Backdate(sentOld, updated: now.AddDays(-40)); // InProgress → excluido

        await SeedAsync(db, oldDraft, oldReady, freshDraft, heldOldDraft, sentOld);

        var stale = await repo.ListStaleUnsentAsync(cutoff, batchSize: 100);

        Assert.Equal(new[] { oldDraft.Id, oldReady.Id }.OrderBy(x => x), stale.Select(r => r.Id).OrderBy(x => x));
    }

    // ---------- helpers ----------

    private static SignatureRequest Draft() =>
        SignatureRequest
            .CreateDraft(
                Tenant,
                Guid.NewGuid(),
                "Consent 2026",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    private static SignatureRequest ReadyWithField()
    {
        var request = Draft();
        var signer = request
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Sam Signer").Value, null)
            .Value;
        var position = FieldPosition.Create(1, 0.5, 0.5, 0.1, 0.05).Value;
        request.PlaceField(signer.Id, SignatureFieldKind.Signature, position, null, false);
        request.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        return request;
    }

    /// <summary>Ajusta timestamps/estado que sólo tienen setter privado, para montar escenarios.</summary>
    private static void Backdate(
        SignatureRequest request,
        DateTime? updated = null,
        DateTime? expires = null,
        bool? legalHold = null
    )
    {
        void Set(string name, object value) =>
            typeof(SignatureRequest)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(request, value);

        if (updated is not null)
            Set(nameof(SignatureRequest.UpdatedAtUtc), updated.Value);
        if (expires is not null)
            Set(nameof(SignatureRequest.ExpiresAtUtc), expires.Value);
        if (legalHold is not null)
            Set(nameof(SignatureRequest.LegalHold), legalHold.Value);
    }

    private static SignatureDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SignatureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SignatureDbContext(options, new StubTenantContext(Tenant));
    }

    private static async Task SeedAsync(SignatureDbContext db, params SignatureRequest[] requests)
    {
        db.SignatureRequests.AddRange(requests);
        await db.SaveChangesAsync();
    }

    private sealed class StubTenantContext(Guid? tenantId = null) : ITenantContext
    {
        public Guid TenantId => tenantId ?? throw new InvalidOperationException("No tenant set.");
        public bool HasTenant => tenantId.HasValue;

        public void SetTenant(Guid value) => throw new NotSupportedException();
    }
}
