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
/// SIG-1: firmar, verificar el PIN o el código, rechazar, consentir y pedir el código sobreviven a un conflicto suelto;
/// agotados los intentos, 409.
/// </summary>
public sealed class PublicSignatureControllerIntegrationTests(SignatureApiFactory baseFactory)
    : IClassFixture<SignatureApiFactory>
{
    private const int MaxAttempts = 3;
    private const string ConflictCode = "Persistence.ConcurrencyConflict";

    public static TheoryData<string, object> PublicWrites =>
        new()
        {
            { "sign", new { Method = "Typed", TypedName = "Jane Signer" } },
            { "verify-pin", new { Pin = "123456" } },
            { "reject", new { Reason = "No" } },
            { "verify-challenge", new { Method = "EmailOtp", Answer = "123456" } },
            { "consent", new { } },
            { "challenge", new { Method = "EmailOtp" } },
        };

    [Theory]
    [MemberData(nameof(PublicWrites))]
    public async Task PublicWrite_ConcurrencyConflictOnce_ResolvesWithoutTheSigner(string action, object body)
    {
        var tokens = new ConflictingTokenService(conflicts: 1);

        var response = await PostAsync(tokens, action, body);

        Assert.Equal(2, tokens.Calls);
        var error = await response.Content.ReadFromJsonAsync<Error>();
        Assert.Equal(ConflictingTokenService.NextAttemptCode, error?.Code);
    }

    [Theory]
    [MemberData(nameof(PublicWrites))]
    public async Task PublicWrite_ConflictOnEveryAttempt_Returns409(string action, object body)
    {
        var tokens = new ConflictingTokenService(conflicts: int.MaxValue);

        var response = await PostAsync(tokens, action, body);

        Assert.Equal(MaxAttempts, tokens.Calls);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<Error>();
        Assert.Equal(ConflictCode, error?.Code);
    }

    private Task<HttpResponseMessage> PostAsync(ISigningTokenService tokens, string action, object body)
    {
        var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<ISigningTokenService>(tokens))
            )
        );
        return factory.CreateClient().PostAsJsonAsync($"/signature/public/any-token/{action}", body);
    }

    private sealed class ConflictingTokenService(int conflicts) : ISigningTokenService
    {
        public const string NextAttemptCode = "Signature.Token.Invalid";
        private int _calls;

        public int Calls => _calls;

        public string Issue(SigningTokenPayload payload) => "token";

        public Result<SigningTokenPayload> Verify(string token)
        {
            if (Interlocked.Increment(ref _calls) <= conflicts)
                throw new ConflictException(ConflictCode, "Concurrent update.");
            return Result.Failure<SigningTokenPayload>(new Error(NextAttemptCode, "Invalid token."));
        }
    }
}
