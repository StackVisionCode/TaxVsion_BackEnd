using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Platform;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-1 contra el SQL Server real, en la base temporal de las T-INT (BB-10): dos
/// escrituras sobre la misma <see cref="SignatureRequest"/> desde lecturas viejas no se pisan; la segunda
/// choca con <c>Persistence.ConcurrencyConflict</c>. Tenant propio y limpieza por TenantId.
/// </summary>
public sealed class SignatureRequestConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string ConflictCode = "Persistence.ConcurrencyConflict";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _connectionString = SignatureTestDatabase.ConnectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.SignatureRequests.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TwoSignersSignInParallel_SecondSaveConflicts_ThenRetryCompletes()
    {
        var (requestId, signerA, signerB) = await SeedInProgressAsync();

        await using var first = NewContext();
        await using var second = NewContext();
        var a = await LoadAsync(first, requestId);
        var b = await LoadAsync(second, requestId);
        Assert.True(Sign(a, signerA).IsSuccess);
        Assert.True(Sign(b, signerB).IsSuccess);

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal(ConflictCode, conflict.Code);

        // Reintento: contexto nuevo, estado nuevo; la segunda firma completa la solicitud.
        await using var retry = NewContext();
        var fresh = await LoadAsync(retry, requestId);
        Assert.True(Sign(fresh, signerB).IsSuccess);
        await retry.SaveChangesAsync();

        Assert.Equal(SignatureRequestStatus.Completed, await ReadStatusAsync(requestId));
    }

    [Fact]
    public async Task ExpirationJobVsLastSignature_JobSaveConflicts()
    {
        var (requestId, signerA, signerB) = await SeedInProgressAsync();
        await using (var seed = NewContext())
        {
            var request = await LoadAsync(seed, requestId);
            Assert.True(Sign(request, signerA).IsSuccess);
            await seed.SaveChangesAsync();
        }

        await using var job = NewContext();
        await using var signing = NewContext();
        var expiring = await LoadAsync(job, requestId);
        var completing = await LoadAsync(signing, requestId);
        Assert.True(expiring.MarkExpired(DateTime.UtcNow).IsSuccess);
        Assert.True(Sign(completing, signerB).IsSuccess);

        await signing.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => job.SaveChangesAsync());
        Assert.Equal(ConflictCode, conflict.Code);
        Assert.Equal(SignatureRequestStatus.Completed, await ReadStatusAsync(requestId));
    }

    [Fact]
    public async Task RejectVsComplete_LastWriterDoesNotWinSilently()
    {
        var (requestId, signerA, signerB) = await SeedInProgressAsync();

        await using var rejecting = NewContext();
        await using var signing = NewContext();
        var r = await LoadAsync(rejecting, requestId);
        var s = await LoadAsync(signing, requestId);
        Assert.True(r.MarkSignerRejected(signerA, DateTime.UtcNow, "No", "127.0.0.1", "tests").IsSuccess);
        Assert.True(Sign(s, signerB).IsSuccess);

        await signing.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => rejecting.SaveChangesAsync());
        Assert.Equal(ConflictCode, conflict.Code);
    }

    [Fact]
    public async Task ParallelPinFailures_EveryFailureCounted()
    {
        var policy = TestSignatureLimits.Verification with { MaxAttempts = 3 };
        var (requestId, signerA, _) = await SeedInProgressAsync(withPin: true);

        // Tres intentos fallidos que arrancan desde la misma lectura; cada uno reintenta si choca.
        var start = new TaskCompletionSource();
        var attempts = Enumerable
            .Range(0, policy.MaxAttempts)
            .Select(_ => FailPinWithRetryAsync(requestId, signerA, policy, start.Task))
            .ToList();
        start.SetResult();
        await Task.WhenAll(attempts);

        await using var db = NewContext();
        var signer = await db.Signers.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == signerA);
        Assert.Equal(policy.MaxAttempts, signer.PinFailedAttempts);
        Assert.NotNull(signer.PinLockedUntilUtc);
    }

    private async Task FailPinWithRetryAsync(Guid requestId, Guid signerId, VerificationPolicy policy, Task start)
    {
        var contexts = new List<SignatureDbContext>();
        try
        {
            // Todos leen antes de que nadie guarde.
            var db = NewContext();
            contexts.Add(db);
            var request = await LoadAsync(db, requestId);
            await start;
            for (var attempt = 1; ; attempt++)
            {
                request.VerifySignerWithPin(signerId, false, DateTime.UtcNow, "127.0.0.1", "tests", policy);
                try
                {
                    await db.SaveChangesAsync();
                    return;
                }
                catch (ConflictException ex) when (ex.Code == ConflictCode && attempt < 10)
                {
                    db = NewContext();
                    contexts.Add(db);
                    request = await LoadAsync(db, requestId);
                }
            }
        }
        finally
        {
            foreach (var context in contexts)
                await context.DisposeAsync();
        }
    }

    private static Result<IReadOnlyList<Guid>> Sign(SignatureRequest request, Guid signerId)
    {
        var signer = request.Signers.Single(s => s.Id == signerId);
        return request.MarkSignerDocumentsCompleted(
            signerId,
            signer.Fields.Select(f => f.DocumentId).Distinct().ToList(),
            DateTime.UtcNow,
            SignatureCaptureMethod.Typed,
            signer.FullName.Value,
            null,
            "127.0.0.1",
            "tests"
        );
    }

    private async Task<(Guid RequestId, Guid SignerA, Guid SignerB)> SeedInProgressAsync(bool withPin = false)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Concurrency", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Return").Value;
        var a = request
            .AddSigner(
                limits,
                SignerEmail.Create("a@example.test").Value,
                SignerFullName.Create("Signer A").Value,
                null
            )
            .Value;
        var b = request
            .AddSigner(
                limits,
                SignerEmail.Create("b@example.test").Value,
                SignerFullName.Create("Signer B").Value,
                null
            )
            .Value;
        foreach (var signerId in new[] { a.Id, b.Id })
            Assert.True(
                request
                    .PlaceField(
                        TestSignatureLimits.MaxFieldsPerSigner,
                        signerId,
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
                    VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 50).Value
                )
                .IsSuccess
        );
        if (withPin)
            Assert.True(request.SetPractitionerPin("hash", Guid.NewGuid(), DateTime.UtcNow).IsSuccess);
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);

        await using var db = NewContext();
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return (request.Id, a.Id, b.Id);
    }

    private Task<SignatureRequest> LoadAsync(SignatureDbContext db, Guid requestId) =>
        new SignatureRequestRepository(db).GetByIdAsync(_tenantId, requestId)!;

    private async Task<SignatureRequestStatus> ReadStatusAsync(Guid requestId)
    {
        await using var db = NewContext();
        return await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r => r.Id == requestId)
            .Select(r => r.Status)
            .SingleAsync();
    }

    private SignatureDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}

/// <summary>Cadena de la base temporal de las T-INT (BB-10).</summary>
internal static class SignatureTestDatabase
{
    public static string ConnectionString => SignatureIntegrationDatabase.ConnectionString;
}
