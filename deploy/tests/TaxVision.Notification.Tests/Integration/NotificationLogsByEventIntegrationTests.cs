using System.Net;
using System.Net.Http.Headers;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using Xunit;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>
/// AJ-08 contra Notification.Api real: solo un token de servicio de la misma oficina lee el estado, y la respuesta no
/// trae destinatario ni asunto.
/// </summary>
public sealed class NotificationLogsByEventIntegrationTests(NotificationApiFactory factory)
    : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task A_service_token_of_the_office_reads_only_the_status()
    {
        var tenantId = Guid.NewGuid();
        var eventId = await SeedSentEmailAsync(tenantId);

        var response = await Client(JwtTestTokenFactory.MintService(factory, tenantId))
            .GetAsync(Url(tenantId, eventId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("""{"status":"Sent"}""", body);
    }

    [Fact]
    public async Task Another_office_or_a_user_token_cannot_read_it()
    {
        var tenantId = Guid.NewGuid();
        var eventId = await SeedSentEmailAsync(tenantId);

        var otherOffice = await Client(JwtTestTokenFactory.MintService(factory, Guid.NewGuid()))
            .GetAsync(Url(tenantId, eventId));
        var user = await Client(JwtTestTokenFactory.Mint(factory, tenantId, Guid.NewGuid()))
            .GetAsync(Url(tenantId, eventId));
        var unknownEvent = await Client(JwtTestTokenFactory.MintService(factory, tenantId))
            .GetAsync(Url(tenantId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, otherOffice.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownEvent.StatusCode);
    }

    private static string Url(Guid tenantId, Guid eventId) =>
        $"/internal/tenants/{tenantId}/notification-logs/by-event/{eventId}";

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> SeedSentEmailAsync(Guid tenantId)
    {
        var eventId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var log = NotificationLog
            .Create(
                tenantId,
                NotificationChannel.Email,
                NotificationRecipient.ForEmail("ana@example.com").Value,
                "Reset",
                "auth.password-reset.office",
                eventId,
                "it"
            )
            .Value;
        log.MarkSent();
        await scope.ServiceProvider.GetRequiredService<INotificationLogRepository>().AddAsync(log);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return eventId;
    }
}
