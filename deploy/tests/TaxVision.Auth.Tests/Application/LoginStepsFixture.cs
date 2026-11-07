using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Login;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Arma los pasos reales del login sobre los dobles de cada prueba.</summary>
internal static class LoginStepsFixture
{
    public static (
        LoginDiscovery Discovery,
        LoginPasswordCheck Password,
        LoginMfaStep Mfa,
        LoginSessionStep Session
    ) Build(
        AccountSessionFixture account,
        IUserLoginLock loginLock,
        IMfaRepository mfa,
        ISessionTakeoverTicketStore tickets,
        ILoginThrottler throttler,
        MfaOptions mfaOptions,
        IUserRepository? users = null,
        IPasswordHasher? hasher = null,
        IAuthAuditWriter? audit = null,
        IRequestContext? request = null,
        ICorrelationContext? correlation = null
    )
    {
        var trail = new AuthAuditTrail(
            audit ?? account.Audit,
            request ?? account.Request,
            correlation ?? account.Correlation
        );
        var session = new LoginSessionStep(account.Roles, account.Issuer, account.Sessions, tickets, trail);
        return (
            new LoginDiscovery(throttler, account.Tenants, users ?? account.Users, trail),
            new LoginPasswordCheck(hasher ?? new PrefixedPasswordHasher(), loginLock, throttler, trail),
            new LoginMfaStep(mfa, account.TokenService, session, trail, Options.Create(mfaOptions)),
            session
        );
    }
}
