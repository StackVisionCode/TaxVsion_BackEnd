using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.CentralLogin.Commands;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Login central de alguien que es empleado y cliente de la misma oficina, con la misma contraseña.</summary>
public sealed class DiscoverLoginHandlerTests
{
    private const string Email = "ana@example.com";

    [Fact]
    public async Task Without_a_kind_both_accounts_of_the_office_are_offered()
    {
        var context = new Context();

        var result = await context.DiscoverAsync(accountKind: null);

        Assert.NotNull(result.Value.Offices);
        Assert.Equal(2, result.Value.Offices!.Count);
        Assert.Contains(result.Value.Offices, office => office.IsClientPortal);
        Assert.Contains(result.Value.Offices, office => !office.IsClientPortal);
    }

    [Fact]
    public async Task The_client_login_only_opens_the_portal_account()
    {
        var context = new Context();

        var result = await context.DiscoverAsync(UserAccountKind.Portal);

        Assert.True(result.Value.IsClientPortal);
        var payload = Assert.Single(context.Tickets.Tickets.Values);
        Assert.Equal(context.Portal.Id, payload.UserId);
    }

    [Fact]
    public async Task Choosing_an_entry_hands_off_that_exact_account()
    {
        var context = new Context();
        var selection = await context.DiscoverAsync(accountKind: null);

        var handoff = await IssueHandoffTicketHandler.Handle(
            new IssueHandoffTicketCommand(
                selection.Value.DiscoverySessionRef!.Value,
                context.World.Tenant.Id,
                AccountKind: UserAccountKind.Portal
            ),
            context.Sessions,
            context.Tickets,
            context.World.Tenants,
            new NoMfaRepository(),
            new UnusedTotpService(),
            new UnusedSecretProtector(),
            context.World.TokenService,
            context.World.Audit,
            context.World.Request,
            context.World.Correlation,
            context.World.UnitOfWork,
            CancellationToken.None
        );

        Assert.True(handoff.IsSuccess);
        Assert.Equal(context.Portal.Id, context.Tickets.Tickets[handoff.Value.Ticket].UserId);
    }

    private sealed class Context
    {
        public AccountSessionFixture World { get; } = new();
        public User Portal { get; }
        public InMemoryDiscoverySessionStore Sessions { get; } = new();
        public InMemoryHandoffTicketStore Tickets { get; } = new();

        public Context()
        {
            World.Users.Add(
                User.Register(
                    World.Tenant.Id,
                    "Ana",
                    "Staff",
                    Email,
                    "hashed:same-pass",
                    UserActorType.TenantEmployee
                ).Value
            );
            Portal = User.Register(
                World.Tenant.Id,
                "Ana",
                "Client",
                Email,
                "hashed:same-pass",
                UserActorType.CustomerPortal,
                Guid.NewGuid()
            ).Value;
            World.Users.Add(Portal);
        }

        public Task<Result<DiscoverLoginResponse>> DiscoverAsync(UserAccountKind? accountKind) =>
            DiscoverLoginHandler.Handle(
                new DiscoverLoginCommand(Email, "same-pass", AccountKind: accountKind),
                World.Users,
                World.Tenants,
                new PrefixedPasswordHasher(),
                new NoMfaRepository(),
                Sessions,
                Tickets,
                new PermissiveLoginThrottler(),
                World.Audit,
                World.Request,
                World.Correlation,
                World.UnitOfWork,
                Options.Create(new MfaOptions { Enforced = false }),
                CancellationToken.None
            );
    }

    private sealed class UnusedTotpService : TaxVision.Auth.Application.Abstractions.ITotpService
    {
        public string GenerateSecret() => throw new NotSupportedException();

        public string BuildOtpAuthUri(string accountName, string base32Secret, string issuer) =>
            throw new NotSupportedException();

        public bool ValidateCode(string base32Secret, string code, DateTime utcNow) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedSecretProtector : BuildingBlocks.Security.ISecretProtector
    {
        public string Protect(string plaintext) => throw new NotSupportedException();

        public bool TryUnprotect(
            string? protectedValue,
            out string plaintext,
            out BuildingBlocks.Security.SecretUnprotectFailure failure
        ) => throw new NotSupportedException();
    }
}
