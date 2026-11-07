using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.Tracking;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-1 paso 3 contra SQL real y el host de Signature: un rival guarda entre la lectura y el guardado del firmante.
/// El intento perdido se revierte entero y el siguiente relee el agregado. Base temporal propia.
/// </summary>
public sealed class PublicSigningConflictIntegrationTests(PublicSigningHostFixture host)
    : IClassFixture<PublicSigningHostFixture>
{
    private const string CorrectPin = "135790";
    private const string WrongPin = "246801";
    private const int MaxAttempts = 3;

    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact(Timeout = 120_000)]
    public async Task LastSignature_LosesAgainstTheOtherSigner_RereadsAndCompletes()
    {
        var seed = await SeedInProgressAsync();
        host.Rival.Arm(1, () => SignAsync(seed.RequestId, seed.SignerA));

        var (response, session) = await PostAsync(
            seed,
            seed.SignerB,
            "sign",
            new { Method = "Typed", TypedName = "Signer B" }
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(SignatureRequestStatus.Completed, await ReadStatusAsync(seed.RequestId));
        Assert.Equal(seed.SignerB, Assert.Single(session.Sent.MessagesOf<DocumentSignedIntegrationEvent>()).SignerId);
        Assert.Single(session.Sent.MessagesOf<SignatureRequestCompletedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task WrongPin_LosesAgainstAnotherFailure_CountsOnce()
    {
        var seed = await SeedInProgressAsync(withPin: true);
        host.Rival.Arm(1, () => FailPinAsync(seed.RequestId, seed.SignerA));

        var (response, session) = await PostAsync(seed, seed.SignerA, "verify-pin", new { Pin = WrongPin });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var failed = Assert.Single(session.Sent.MessagesOf<SignerPinFailedIntegrationEvent>());
        Assert.Equal(2, failed.FailedAttempts);
        Assert.Equal(2, await ReadPinFailuresAsync(seed.SignerA));
    }

    [Fact(Timeout = 120_000)]
    public async Task CorrectPin_LosesOnce_VerifiesTheSigner()
    {
        var seed = await SeedInProgressAsync(withPin: true);
        host.Rival.Arm(1, () => BumpAsync(seed.RequestId));

        var (response, session) = await PostAsync(seed, seed.SignerA, "verify-pin", new { Pin = CorrectPin });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(session.Sent.MessagesOf<SignerPinVerifiedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Reject_LosesOnce_RejectsAndNotifiesOnce()
    {
        var seed = await SeedInProgressAsync();
        host.Rival.Arm(1, () => BumpAsync(seed.RequestId));

        var (response, session) = await PostAsync(seed, seed.SignerA, "reject", new { Reason = "No" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(SignatureRequestStatus.Rejected, await ReadStatusAsync(seed.RequestId));
        Assert.Single(session.Sent.MessagesOf<SignerRejectedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task WrongPin_LosesEveryAttempt_Returns409AndCountsNothing()
    {
        var seed = await SeedInProgressAsync(withPin: true);
        host.Rival.Arm(int.MaxValue, () => BumpAsync(seed.RequestId));

        var (response, session) = await PostAsync(seed, seed.SignerA, "verify-pin", new { Pin = WrongPin });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Persistence.ConcurrencyConflict", (await response.Content.ReadFromJsonAsync<Error>())?.Code);
        Assert.Equal(MaxAttempts, host.Rival.Collisions);
        Assert.Equal(0, await ReadPinFailuresAsync(seed.SignerA));
        Assert.Empty(session.Sent.MessagesOf<SignerPinFailedIntegrationEvent>());
    }

    private async Task<(HttpResponseMessage Response, ITrackedSession Session)> PostAsync(
        Seed seed,
        Guid signerId,
        string action,
        object body
    )
    {
        host.Tokens.Payload = new SigningTokenPayload(
            _tenantId,
            seed.RequestId,
            signerId,
            seed.RevocationEpoch,
            null,
            Guid.NewGuid().ToString("N")
        );
        var client = host.Factory.CreateClient();
        HttpResponseMessage? response = null;
        var session = await host
            .Factory.Services.GetRequiredService<IHost>()
            .TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .Timeout(TimeSpan.FromSeconds(60))
            .ExecuteAndWaitAsync(
                (Func<IMessageContext, Task>)(
                    async _ => response = await client.PostAsJsonAsync($"/signature/public/any-token/{action}", body)
                )
            );
        return (response!, session);
    }

    private async Task SignAsync(Guid requestId, Guid signerId)
    {
        await using var db = host.Database.NewContext(_tenantId);
        var request = await LoadAsync(db, requestId);
        var signer = request.Signers.Single(s => s.Id == signerId);
        Assert.True(
            request
                .MarkSignerDocumentsCompleted(
                    signerId,
                    signer.Fields.Select(f => f.DocumentId).Distinct().ToList(),
                    DateTime.UtcNow,
                    SignatureCaptureMethod.Typed,
                    signer.FullName.Value,
                    null,
                    "127.0.0.1",
                    "tests"
                )
                .IsSuccess
        );
        await db.SaveChangesAsync();
    }

    private async Task FailPinAsync(Guid requestId, Guid signerId)
    {
        await using var db = host.Database.NewContext(_tenantId);
        var request = await LoadAsync(db, requestId);
        request.VerifySignerWithPin(
            signerId,
            false,
            DateTime.UtcNow,
            "127.0.0.1",
            "tests",
            TestSignatureLimits.Verification
        );
        await db.SaveChangesAsync();
    }

    private async Task BumpAsync(Guid requestId)
    {
        await using var db = host.Database.NewContext(_tenantId);
        await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r => r.Id == requestId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.UpdatedAtUtc, DateTime.UtcNow));
    }

    private async Task<int> ReadPinFailuresAsync(Guid signerId)
    {
        await using var db = host.Database.NewContext(_tenantId);
        return await db
            .Signers.IgnoreQueryFilters()
            .Where(s => s.Id == signerId)
            .Select(s => s.PinFailedAttempts)
            .SingleAsync();
    }

    private async Task<SignatureRequestStatus> ReadStatusAsync(Guid requestId)
    {
        await using var db = host.Database.NewContext(_tenantId);
        return await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r => r.Id == requestId)
            .Select(r => r.Status)
            .SingleAsync();
    }

    private Task<SignatureRequest> LoadAsync(SignatureDbContext db, Guid requestId) =>
        new SignatureRequestRepository(db).GetByIdAsync(_tenantId, requestId)!;

    private async Task<Seed> SeedInProgressAsync(bool withPin = false)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Conflict", null, "Fiscal", 72, false, false, false)
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
        {
            var hash = host.Factory.Services.GetRequiredService<IPinHasher>().Hash(CorrectPin);
            Assert.True(request.SetPractitionerPin(hash, Guid.NewGuid(), DateTime.UtcNow).IsSuccess);
        }
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);

        await using var db = host.Database.NewContext(_tenantId);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return new Seed(request.Id, a.Id, b.Id, request.RevocationEpoch);
    }

    private sealed record Seed(Guid RequestId, Guid SignerA, Guid SignerB, int RevocationEpoch);
}

/// <summary>Host real de Signature sobre una base temporal, con el token y el rival bajo control de la prueba.</summary>
public sealed class PublicSigningHostFixture : IAsyncLifetime
{
    private TemporaryDatabaseFactory? _factory;

    internal SignatureTemporaryDatabase Database { get; } = new();
    internal RivalSaveInterceptor Rival { get; } = new();
    internal FixedTokenService Tokens { get; } = new();
    internal SignatureApiFactory Factory => _factory!;

    public async Task InitializeAsync()
    {
        await Database.CreateAsync();
        _factory = new TemporaryDatabaseFactory(Database.ConnectionString, Tokens, Rival);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await Database.DropAsync();
    }

    private sealed class TemporaryDatabaseFactory(
        string connectionString,
        ISigningTokenService tokens,
        IInterceptor rival
    ) : SignatureApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton(tokens));
                services.ConfigureDbContext<SignatureDbContext>(db => db.AddInterceptors(rival));
            });
        }
    }
}

/// <summary>Deja que un rival guarde justo antes del guardado del handler: un choque real de <c>RowVersion</c>.</summary>
internal sealed class RivalSaveInterceptor : SaveChangesInterceptor
{
    private int _remaining;
    private Func<Task> _rival = () => Task.CompletedTask;

    public int Collisions { get; private set; }

    public void Arm(int collisions, Func<Task> rival)
    {
        _remaining = collisions;
        _rival = rival;
        Collisions = 0;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        var tracker = eventData.Context!.ChangeTracker;
        if (_remaining > 0 && tracker.Entries<SignatureRequest>().Any() && tracker.HasChanges())
        {
            _remaining--;
            Collisions++;
            await _rival();
        }
        return result;
    }
}

internal sealed class FixedTokenService : ISigningTokenService
{
    public SigningTokenPayload? Payload { get; set; }

    public string Issue(SigningTokenPayload payload) => "token";

    public Result<SigningTokenPayload> Verify(string token) => Result.Success(Payload!);
}
