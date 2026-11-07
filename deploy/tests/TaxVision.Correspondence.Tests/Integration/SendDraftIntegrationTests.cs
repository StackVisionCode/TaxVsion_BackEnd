using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Audit;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>COR-08: el envío corre sin transacción abierta y un resultado perdido no reenvía.</summary>
public sealed class SendDraftIntegrationTests(CorrespondenceMediatorFactory factory)
    : IClassFixture<CorrespondenceMediatorFactory>,
        IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        factory.Postmaster.Reset();
        factory.AuditLogs.Reset();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.Drafts.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private async Task<Draft> SeedAsync()
    {
        var draft = Draft.CreateNew(_tenantId, null, Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave(
            "Subject",
            "<p>body</p>",
            null,
            [new DraftRecipientData(EmailAddress.Create("to@cor08.test").Value, EmailRecipientType.To, null)]
        );
        await using var seed = CorrespondenceSqlServer.NewContext(_tenantId);
        seed.Drafts.Add(draft);
        await seed.SaveChangesAsync();
        return draft;
    }

    private async Task<string> ReadStatusFromAnotherConnectionAsync(Guid draftId)
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 2000");
        try
        {
            var draft = await db.Drafts.AsNoTracking().FirstAsync(d => d.Id == draftId);
            return draft.Status.ToString();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1222)
        {
            return "row locked by an open transaction";
        }
    }

    private async Task<Result<SendDraftResult>> SendAsync(Draft draft)
    {
        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        return await bus.InvokeAsync<Result<SendDraftResult>>(
            new SendDraftCommand(_tenantId, draft.Id, Guid.NewGuid(), "corr-cor08")
        );
    }

    [Fact]
    public async Task SendingIsCommittedBeforePostmasterIsCalled()
    {
        var draft = await SeedAsync();
        string? seenByAnotherConnection = null;
        factory.Postmaster.OnSend = async () =>
            seenByAnotherConnection = await ReadStatusFromAnotherConnectionAsync(draft.Id);

        await SendAsync(draft);

        Assert.Equal(nameof(DraftStatus.Sending), seenByAnotherConnection);
    }

    [Fact]
    public async Task FailureAfterPostmasterAccepted_DoesNotResend()
    {
        var draft = await SeedAsync();
        factory.AuditLogs.FailNext = true;

        // El resultado no se guarda: la petición falla con el correo ya aceptado.
        try
        {
            await SendAsync(draft);
        }
        catch (InvalidOperationException) { }
        Assert.Equal(1, factory.Postmaster.EffectiveSends);

        await using (var read = CorrespondenceSqlServer.NewContext(_tenantId))
            Assert.Equal(
                DraftStatus.Sending,
                (await read.Drafts.AsNoTracking().FirstAsync(d => d.Id == draft.Id)).Status
            );

        // El usuario vuelve a apretar Enviar: no sale otra vez.
        var again = await SendAsync(draft);
        Assert.True(again.IsFailure);

        using (var scope = factory.Services.CreateScope())
        {
            var stuck = await scope
                .ServiceProvider.GetRequiredService<IDraftRepository>()
                .ListStuckSendingAsync(DateTime.UtcNow.AddMinutes(1), 500);
            Assert.Contains(stuck, x => x.TenantId == _tenantId && x.DraftId == draft.Id);
            await scope.ServiceProvider.GetRequiredService<DraftSendReconciler>().ReconcileAsync(_tenantId, draft.Id);
        }

        Assert.Equal(1, factory.Postmaster.EffectiveSends);
        await using var check = CorrespondenceSqlServer.NewContext(_tenantId);
        var closed = await check.Drafts.AsNoTracking().FirstAsync(d => d.Id == draft.Id);
        Assert.Equal(DraftStatus.Sent, closed.Status);
        Assert.NotNull(closed.SentMessageId);
    }
}

/// <summary>Como Postmaster: la misma clave devuelve el envío existente y solo la primera sale.</summary>
public sealed class IdempotentPostmaster : IPostmasterClient
{
    private readonly Dictionary<Guid, Guid> _sent = [];

    public Func<Task>? OnSend { get; set; }
    public int EffectiveSends { get; private set; }
    public int Calls { get; private set; }

    public void Reset()
    {
        _sent.Clear();
        OnSend = null;
        EffectiveSends = 0;
        Calls = 0;
    }

    public async Task<Result<SendDraftPostmasterResult>> SendAsync(
        Guid tenantId,
        Guid draftId,
        Guid accountId,
        string subject,
        string html,
        string? text,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        IReadOnlyList<DraftAttachmentRef> attachments,
        ReplyContext? replyContext,
        CancellationToken ct = default
    )
    {
        Calls++;
        if (OnSend is { } onSend)
            await onSend();
        if (_sent.TryGetValue(draftId, out var existing))
            return Result.Success(new SendDraftPostmasterResult(existing, null));

        var sentMessageId = Guid.NewGuid();
        _sent[draftId] = sentMessageId;
        EffectiveSends++;
        return Result.Success(new SendDraftPostmasterResult(sentMessageId, "provider-1"));
    }
}

/// <summary>Hace fallar una vez el registro del resultado, después de que Postmaster aceptó.</summary>
public sealed class FlakyAuditLogRepository : ICorrespondenceAuditLogRepository
{
    public bool FailNext { get; set; }

    public void Reset() => FailNext = false;

    public Task AddAsync(CorrespondenceAuditLog entity, CancellationToken ct = default)
    {
        if (!FailNext)
            return Task.CompletedTask;
        FailNext = false;
        throw new InvalidOperationException("Simulated failure while recording the send result.");
    }
}
