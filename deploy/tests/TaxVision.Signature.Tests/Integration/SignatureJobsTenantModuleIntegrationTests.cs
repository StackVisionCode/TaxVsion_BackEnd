using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.RateLimiting;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// Una oficina sin el módulo de firmas no manda recordatorios ni envíos agendados. El filtro vive en la consulta:
/// sin proyección se incluye; con proyección sin el módulo se excluye, y sus agendados no tapan a las demás.
/// </summary>
public sealed class SignatureJobsTenantModuleIntegrationTests(SignatureJobsDatabaseFixture fixture)
    : IClassFixture<SignatureJobsDatabaseFixture>
{
    private readonly Guid _suspended = Guid.NewGuid();
    private readonly Guid _active = Guid.NewGuid();
    private readonly Guid _unknown = Guid.NewGuid();

    [Fact(Timeout = 120_000)]
    public async Task ReminderCandidates_SkipTheOfficeWithoutTheModule()
    {
        await SeedProjectionAsync(_suspended, "campaigns");
        await SeedProjectionAsync(_active, "signatures");
        var suspended = await SeedInProgressAsync(_suspended);
        var active = await SeedInProgressAsync(_active);
        var unknown = await SeedInProgressAsync(_unknown);

        await using var db = fixture.Database.NewContext(Guid.Empty);
        var ids = (await new SignatureRequestRepository(db).ListReminderCandidatesAsync(DateTime.UtcNow, 10))
            .Select(r => r.Id)
            .ToList();

        Assert.DoesNotContain(suspended, ids);
        Assert.Contains(active, ids);
        Assert.Contains(unknown, ids);
    }

    [Fact(Timeout = 120_000)]
    public async Task ScheduledReady_TheSuspendedOfficeDoesNotStarveOthers_AndGoesOutOnReactivation()
    {
        await SeedProjectionAsync(_suspended);
        var late = await SeedScheduledAsync(_suspended, minutesAgo: 30);
        var active = await SeedScheduledAsync(_active, minutesAgo: 5);

        await using (var db = fixture.Database.NewContext(Guid.Empty))
        {
            var first = Assert.Single(
                await new SignatureRequestRepository(db).ListScheduledReadyToSendAsync(DateTime.UtcNow, 1)
            );
            Assert.Equal(active, first.Id);
        }

        await ReactivateAsync(_suspended);

        await using var after = fixture.Database.NewContext(Guid.Empty);
        var ids = (await new SignatureRequestRepository(after).ListScheduledReadyToSendAsync(DateTime.UtcNow, 50))
            .Select(r => r.Id)
            .ToList();
        Assert.Contains(late, ids);
    }

    private async Task SeedProjectionAsync(Guid tenantId, params string[] modules)
    {
        var projection = TenantPlanCodeProjection.Create(tenantId, "pro", 1);
        projection.ApplyIfNewer("pro", 1, modules);
        await using var db = fixture.Database.NewContext(tenantId);
        db.TenantPlanCodeProjections.Add(projection);
        await db.SaveChangesAsync();
    }

    private async Task ReactivateAsync(Guid tenantId)
    {
        await using var db = fixture.Database.NewContext(tenantId);
        var projection = await db
            .TenantPlanCodeProjections.IgnoreQueryFilters()
            .SingleAsync(p => p.TenantId == tenantId);
        projection.ApplyIfNewer("pro", 2, ["signatures"]);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedInProgressAsync(Guid tenantId)
    {
        var request = NewRequest(tenantId);
        Assert.True(request.Send(DateTime.UtcNow.AddHours(-100)).IsSuccess);
        foreach (var due in request.SignersDueForInvitation())
            request.RotateSignerToken(due.Id, Guid.NewGuid().ToString("N"));
        return await SaveAsync(tenantId, request);
    }

    private async Task<Guid> SeedScheduledAsync(Guid tenantId, int minutesAgo)
    {
        var request = NewRequest(tenantId);
        var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
        Assert.True(request.ScheduleSend(at, at.AddMinutes(-1)).IsSuccess);
        return await SaveAsync(tenantId, request);
    }

    private async Task<Guid> SaveAsync(Guid tenantId, SignatureRequest request)
    {
        await using var db = fixture.Database.NewContext(tenantId);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private static SignatureRequest NewRequest(Guid tenantId)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, tenantId, Guid.NewGuid(), "Job", null, "Fiscal", 168, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Return").Value;
        var signer = request
            .AddSigner(
                limits,
                SignerEmail.Create("s@example.test").Value,
                SignerFullName.Create("Signer One").Value,
                null
            )
            .Value;
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    document.Id,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
        Assert.True(
            request
                .AttachVerifiedDocument(
                    document.Id,
                    VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
                )
                .IsSuccess
        );
        return request;
    }
}

/// <summary>Base temporal propia: los listados de los jobs son entre oficinas y no deben ver datos de la flota.</summary>
public sealed class SignatureJobsDatabaseFixture : IAsyncLifetime
{
    internal SignatureTemporaryDatabase Database { get; } = new();

    public Task InitializeAsync() => Database.CreateAsync();

    public Task DisposeAsync() => Database.DropAsync();
}
