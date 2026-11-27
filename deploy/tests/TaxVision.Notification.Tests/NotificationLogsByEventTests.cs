using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Api.Controllers;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Notifications.Queries;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.Lifecycle;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Estado de entrega por evento (AJ-08): Auth pregunta por el EventId del correo y recibe solo el estado, nunca
/// destinatario ni contenido. Solo M2M y bloqueado en el Gateway por el segmento <c>internal</c>.
/// </summary>
public sealed class NotificationLogsByEventTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Returns_only_the_status_of_the_email()
    {
        var eventId = Guid.NewGuid();
        var reader = new FixedReader(NotificationStatus.Delivered);

        var result = await GetNotificationDeliveryStatusHandler.Handle(
            new GetNotificationDeliveryStatusQuery(TenantId, eventId),
            reader,
            CancellationToken.None
        );

        Assert.Equal("Delivered", result.Value.Status);
        Assert.Equal((TenantId, eventId), reader.LastCall);
        Assert.Equal(
            ["Status"],
            typeof(NotificationDeliveryStatusResponse).GetProperties().Select(p => p.Name).ToArray()
        );
    }

    [Fact]
    public async Task An_event_without_email_of_this_office_is_not_found()
    {
        var result = await GetNotificationDeliveryStatusHandler.Handle(
            new GetNotificationDeliveryStatusQuery(TenantId, Guid.NewGuid()),
            new FixedReader(null),
            CancellationToken.None
        );

        Assert.Equal(GetNotificationDeliveryStatusHandler.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public void The_endpoint_is_internal_and_service_only()
    {
        var controller = typeof(InternalNotificationLogsController);
        var route = controller.GetCustomAttribute<RouteAttribute>()!.Template;

        Assert.StartsWith("internal/tenants/{tenantId:guid}/notification-logs", route);
        Assert.Equal("ServiceOnly", controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(
            new[] { ActorType.Service },
            controller.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
    }

    // IA-02: sin subdominio, agotados los reintentos, queda una fila Failed consultable por el EventId.
    [Fact]
    public async Task A_reset_that_never_found_the_office_subdomain_leaves_a_failed_trail()
    {
        var logs = new MemoryLogs();
        var evt = new PasswordResetRequestedIntegrationEvent
        {
            TenantId = TenantId,
            UserId = Guid.NewGuid(),
            Email = "ana@example.com",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24),
            Origin = "Administrative",
        };

        var recorded = await UnsentAccountEmailTrail.RecordAsync(
            evt,
            new TenantHostUnavailableException(TenantId, TenantHostUnavailableReason.NoActiveSubdomain),
            logs,
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(recorded);
        var log = Assert.Single(logs.Added);
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal(evt.EventId, log.RelatedEventId);
        Assert.Equal(TenantId, log.TenantId);
        Assert.Equal(NotificationChannel.Email, log.Channel);
    }

    [Fact]
    public async Task Other_messages_leave_no_trail()
    {
        var logs = new MemoryLogs();

        var recorded = await UnsentAccountEmailTrail.RecordAsync(
            new object(),
            new TenantHostUnavailableException(TenantId, TenantHostUnavailableReason.Transient),
            logs,
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.False(recorded);
        Assert.Empty(logs.Added);
    }

    private sealed class FixedReader(NotificationStatus? status) : INotificationDeliveryStatusReader
    {
        public (Guid, Guid)? LastCall { get; private set; }

        public Task<NotificationStatus?> GetEmailStatusByEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            CancellationToken ct = default
        )
        {
            LastCall = (tenantId, relatedEventId);
            return Task.FromResult(status);
        }
    }

    private sealed class MemoryLogs : INotificationLogRepository
    {
        public List<NotificationLog> Added { get; } = [];

        public Task AddAsync(NotificationLog log, CancellationToken ct = default)
        {
            Added.Add(log);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>((Added, Added.Count));

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);
    }
}
