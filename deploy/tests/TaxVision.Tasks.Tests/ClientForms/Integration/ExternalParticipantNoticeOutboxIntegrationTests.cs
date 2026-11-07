extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using JasperFx;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Tasks.Tests.ClientForms.Integration;

/// <summary>
/// BB-17. <c>Senders</c> recibe por constructor el bus que el consumer recibe por parámetro, no otro del contenedor:
/// host real, handler real y medido en lo entregado por una cola local durable.
/// </summary>
public sealed class ExternalParticipantNoticeOutboxIntegrationTests : IAsyncLifetime
{
    private readonly DeliveredProbe _delivered = new();
    private readonly HandlerFailureSwitch _failure = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _participantId;

    public async Task InitializeAsync()
    {
        _factory = new RetryFormActionRunConcurrencyTests.TasksApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(_delivered);
                services.AddSingleton(_failure);
                services.ConfigureWolverine(options =>
                {
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                    options.Discovery.IncludeType(typeof(NoticeProbeHandler));
                    options.Policies.AddMiddleware(
                        typeof(FailAfterHandlerMiddleware),
                        chain => chain.MessageType == typeof(FormRequestReturnedIntegrationEvent)
                    );
                    options
                        .PublishMessage<FormParticipantNotificationRequestedIntegrationEvent>()
                        .ToLocalQueue("bb17-probe")
                        .UseDurableInbox();
                });
            })
        );

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var participant = FormParticipant
            .Create(
                _tenantId,
                "bb17@example.test",
                "Guest",
                null,
                FormParticipantSource.DirectedRequest,
                DateTime.UtcNow
            )
            .Value;
        db.Add(participant);
        await db.SaveChangesAsync();
        _participantId = participant.Id;
    }

    public async Task DisposeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<TasksDbContext>()
                .Set<FormParticipant>()
                .IgnoreQueryFilters()
                .Where(participant => participant.TenantId == _tenantId)
                .ExecuteDeleteAsync();
        await _factory.DisposeAsync();
    }

    private async Task InvokeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new FormRequestReturnedIntegrationEvent
                {
                    TenantId = _tenantId,
                    CorrelationId = "bb-17",
                    FormRequestId = Guid.NewGuid(),
                    Participant = new ClientFormParticipant { Kind = "External", ParticipantId = _participantId },
                    Title = "Intake",
                    RevisionNumber = 1,
                    FieldCommentCount = 1,
                    ReturnedByUserId = Guid.NewGuid(),
                }
            );
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_requesting_the_notice_delivers_nothing()
    {
        _failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(InvokeAsync);
        await _delivered.SettleAsync(expected: 1);

        Assert.Empty(_delivered.Of<FormParticipantNotificationRequestedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_delivers_the_notice()
    {
        await InvokeAsync();
        await _delivered.SettleAsync(expected: 1);

        Assert.Single(_delivered.Of<FormParticipantNotificationRequestedIntegrationEvent>());
    }

    public static class NoticeProbeHandler
    {
        public static void Handle(FormParticipantNotificationRequestedIntegrationEvent message, DeliveredProbe probe) =>
            probe.Add(message);
    }
}
