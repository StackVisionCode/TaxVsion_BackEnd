using BuildingBlocks.Results;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Users.Login;

/// <summary>Quién intenta entrar, una vez encontrados su oficina y su cuenta, y desde qué superficie.</summary>
public sealed record LoginAttempt(Tenant Tenant, User User, string? DeviceName, SessionSurface Surface);

/// <summary>
/// Cómo termina el login. Un paso solo decide: <c>LoginHandler</c> guarda y después publica <see cref="Notice"/>,
/// con su propio bus, que es el del contexto del mensaje.
/// </summary>
public sealed record LoginDecision(Result<LoginResponse> Outcome, bool MustSave, object? Notice)
{
    public static LoginDecision Refused(Error error, object? notice = null) =>
        new(Result.Failure<LoginResponse>(error), MustSave: true, notice);

    public static LoginDecision RefusedWithoutSaving(Error error) =>
        new(Result.Failure<LoginResponse>(error), MustSave: false, Notice: null);

    public static LoginDecision Answered(LoginResponse response, object? notice = null) =>
        new(Result.Success(response), MustSave: true, notice);
}
