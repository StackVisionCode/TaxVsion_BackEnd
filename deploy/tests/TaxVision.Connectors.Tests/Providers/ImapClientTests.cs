using BuildingBlocks.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Tests.OAuth;
using ImapProviderClient = TaxVision.Connectors.Infrastructure.Providers.Imap.ImapClient;

namespace TaxVision.Connectors.Tests.Providers;

public class ImapClientTests
{
    [Fact]
    public async Task GetHistoryAsync_WhenOneAccountTripsTheBreaker_OtherAccountsStillConnect()
    {
        // Sin credenciales guardadas cada intento falla antes de abrir un socket.
        await using var client = new ImapProviderClient(
            new FakeImapCredentialsRepository(),
            new FakeEncryptedSecretProtector(),
            new NoWaitProviderRateLimiter(),
            new HttpResiliencePipelineRegistry(minimumThroughput: 3),
            NullLogger<ImapProviderClient>.Instance
        );
        var tenantId = Guid.NewGuid();
        var brokenAccount = Guid.NewGuid();

        for (var attempt = 0; attempt < 3; attempt++)
            await Assert.ThrowsAsync<EmailProviderException>(() =>
                client.GetHistoryAsync(brokenAccount, tenantId, null)
            );
        var tripped = await Assert.ThrowsAsync<EmailProviderException>(() =>
            client.GetHistoryAsync(brokenAccount, tenantId, null)
        );
        var otherAccount = await Assert.ThrowsAsync<EmailProviderException>(() =>
            client.GetHistoryAsync(Guid.NewGuid(), tenantId, null)
        );

        Assert.Contains("circuit breaker is open", tripped.Message);
        // La otra cuenta llegó a intentar su propia conexión: no la frenó el breaker ajeno.
        Assert.Contains("Could not load IMAP credentials", otherAccount.Message);
    }
}
