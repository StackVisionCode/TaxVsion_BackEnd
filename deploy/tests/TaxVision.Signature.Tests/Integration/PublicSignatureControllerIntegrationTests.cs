using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-1: un conflicto de concurrencia en la firma pública se reintenta en línea con un intento nuevo
/// (scope y transacción nuevos) en vez de responder 500/409 al firmante.
/// </summary>
public sealed class PublicSignatureControllerIntegrationTests(SignatureApiFactory baseFactory)
    : IClassFixture<SignatureApiFactory>
{
    [Fact]
    public async Task PublicSubmitSignature_ConcurrencyConflictOnce_RetriesInlineAndReturnsHandlerResult()
    {
        var tokens = new ConflictOnceTokenService();
        var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<ISigningTokenService>(tokens))
            )
        );
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/signature/public/any-token/sign",
            new { Method = "Typed", TypedName = "Jane Signer" }
        );

        // El segundo intento llega al handler y responde su propio error de token, no un conflicto.
        Assert.Equal(2, tokens.Calls);
        Assert.NotEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<Error>();
        Assert.Equal(ConflictOnceTokenService.SecondAttemptCode, error?.Code);
    }

    private sealed class ConflictOnceTokenService : ISigningTokenService
    {
        public const string SecondAttemptCode = "Signature.Token.Invalid";
        private int _calls;

        public int Calls => _calls;

        public string Issue(SigningTokenPayload payload) => "token";

        public Result<SigningTokenPayload> Verify(string token)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new ConflictException("Persistence.ConcurrencyConflict", "Concurrent update.");
            return Result.Failure<SigningTokenPayload>(new Error(SecondAttemptCode, "Invalid token."));
        }
    }
}
