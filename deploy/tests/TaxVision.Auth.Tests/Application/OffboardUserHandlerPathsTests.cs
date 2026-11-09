using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Offboarding;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Offboarding;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// AUTH-7: caracteriza las salidas del retiro que no cubría <c>UserManagementCommandsTests</c>: sucesor,
/// idempotencia, corte de sesión y orden de los avisos. La llamada al handler vive en <see cref="OffboardWorld"/>.
/// </summary>
public sealed class OffboardUserHandlerPathsTests
{
    [Fact]
    public async Task Nobody_removes_their_own_account()
    {
        var world = new OffboardWorld();
        var target = world.AddEmployee();

        var result = await world.OffboardAsync(target, requestedBy: target.Id);

        Assert.Equal("User.SelfAction", result.Error.Code);
        world.AssertUntouched(target);
    }

    [Fact]
    public async Task A_user_of_another_office_does_not_exist()
    {
        var world = new OffboardWorld();
        var foreign = User.Register(
            Guid.NewGuid(),
            "Bo",
            "Other",
            "bo@example.com",
            "hashed:x",
            UserActorType.TenantEmployee
        ).Value;
        world.Account.Users.Add(foreign);

        var result = await world.OffboardAsync(foreign);

        Assert.Equal("User.NotFound", result.Error.Code);
        world.AssertUntouched(foreign);
    }

    [Fact]
    public async Task Offboarding_twice_publishes_only_once()
    {
        var world = new OffboardWorld();
        var target = world.AddEmployee();
        await world.OffboardAsync(target);
        var published = world.Bus.Published.Count;
        var saves = world.UnitOfWork.SaveChangesCallCount;

        var again = await world.OffboardAsync(target);

        Assert.True(again.IsSuccess);
        Assert.Equal(published, world.Bus.Published.Count);
        Assert.Equal(saves, world.UnitOfWork.SaveChangesCallCount);
        Assert.Single(world.Runs.Runs);
    }

    public static TheoryData<string> InvalidSuccessors => ["self", "missing", "foreign", "inactive", "portal"];

    [Theory]
    [MemberData(nameof(InvalidSuccessors))]
    public async Task An_invalid_successor_stops_the_offboarding_before_touching_the_user(string kind)
    {
        var world = new OffboardWorld();
        var target = world.AddEmployee();

        var result = await world.OffboardAsync(target, successorId: world.SuccessorOfKind(kind, target));

        Assert.Equal("User.Successor", result.Error.Code);
        Assert.Equal("The successor must be an active employee of this tenant.", result.Error.Message);
        world.AssertUntouched(target);
    }

    [Fact]
    public async Task A_bad_reason_is_refused_after_the_successor_check()
    {
        var world = new OffboardWorld();
        var target = world.AddEmployee();

        var result = await world.OffboardAsync(
            target,
            successorId: Guid.NewGuid(),
            note: new string('x', OffboardingDetails.ReasonNoteMaxLength + 1)
        );

        Assert.Equal("User.Successor", result.Error.Code);
        world.AssertUntouched(target);
    }

    [Fact]
    public async Task A_valid_offboarding_cuts_access_saves_once_and_announces_after_saving()
    {
        var world = new OffboardWorld();
        var target = world.AddEmployee();
        var successor = world.AddEmployee("successor@example.com");
        var actor = Guid.NewGuid();
        var session = await world.OpenSessionAsync(target);

        var result = await world.OffboardAsync(target, requestedBy: actor, successorId: successor.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Offboarded, target.Status);
        Assert.Contains(session, world.Account.Denylist.Denied);
        Assert.Equal(session, Assert.Single(world.Revocations.Revoked));
        Assert.DoesNotContain(world.Account.Sessions.Sessions, item => item.UserId == target.Id && item.IsActive);
        Assert.Equal(1, world.UnitOfWork.SaveChangesCallCount);
        Assert.Equal(0, world.UnitOfWork.PublishedAtLastSave);

        var log = Assert.Single(world.Account.Audit.Logs);
        Assert.Equal(AuthAuditAction.UserOffboarded, log.Action);
        Assert.True(log.Success);
        Assert.Equal(actor, log.UserId);
        Assert.Equal("User", log.TargetType);
        Assert.Equal(target.Id, log.TargetId);
        Assert.Equal(
            $$"""{"mfaCleared":false,"cancelledInvitations":0,"successorUserId":"{{successor.Id}}"}""",
            log.DetailsJson
        );

        var offboarded = Assert.IsType<UserOffboardedIntegrationEvent>(Assert.Single(world.Bus.Published));
        Assert.Equal(target.TenantId, offboarded.TenantId);
        Assert.Equal(target.Id, offboarded.UserId);
        Assert.Equal(target.Email, offboarded.Email);
        Assert.Equal(target.ActorType.ToString(), offboarded.ActorType);
        Assert.Equal(actor, offboarded.OffboardedByUserId);
        Assert.Equal(successor.Id, offboarded.SuccessorUserId);
        Assert.Equal(target.RemovedAtUtc, offboarded.RemovedAtUtc);
        Assert.Equal(world.Account.Correlation.CorrelationId, offboarded.CorrelationId);
    }

    [Fact]
    public async Task The_record_keeps_the_reason_and_the_note_without_blanks()
    {
        var world = new OffboardWorld();
        var target = world.AddEmployee();
        var actor = Guid.NewGuid();

        await world.OffboardAsync(target, requestedBy: actor, reason: OffboardingReason.Resigned, note: "  left  ");

        var details = Assert.Single(world.Runs.Runs);
        Assert.Equal(actor, details.OffboardedByUserId);
        Assert.Equal(OffboardingReason.Resigned, details.Reason);
        Assert.Equal("left", details.ReasonNote);
    }

    [Fact]
    public async Task With_an_avatar_the_notices_go_in_order()
    {
        var world = new OffboardWorld();
        var target = world.AddEmployee();
        AvatarDoubles.GiveConfirmedAvatar(target);

        await world.OffboardAsync(target);

        Assert.Equal(
            [
                typeof(UserOffboardedIntegrationEvent),
                typeof(FilesDeletionRequestedIntegrationEvent),
                typeof(UserProfileUpdatedIntegrationEvent),
            ],
            world.Bus.Published.Select(message => message.GetType())
        );
    }

    [Fact]
    public async Task Mfa_and_cancelled_invitations_show_in_the_audit_details()
    {
        var world = new OffboardWorld();
        var target = world.AddEmployee();
        world.Mfa.Enroll(target);

        await world.OffboardAsync(target);

        Assert.False(target.MfaEnabled);
        Assert.Empty(world.Mfa.Methods);
        Assert.Equal(
            """{"mfaCleared":true,"cancelledInvitations":0,"successorUserId":null}""",
            Assert.Single(world.Account.Audit.Logs).DetailsJson
        );
    }

    private sealed class OffboardWorld
    {
        public OffboardWorld() => UnitOfWork = new ProbeUnitOfWork(Bus);

        public AccountSessionFixture Account { get; } = new();
        public InMemoryMfaRepository Mfa { get; } = new();
        public InMemoryInvitationRepository Invitations { get; } = new();
        public InMemoryOffboardingRunRepository Runs { get; } = new();
        public RecordingRevocations Revocations { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        public ProbeUnitOfWork UnitOfWork { get; }

        public User AddEmployee(string email = "leaver@example.com")
        {
            var user = User.Register(
                Account.Tenant.Id,
                "Eli",
                "Employee",
                email,
                "hashed:x",
                UserActorType.TenantEmployee
            ).Value;
            Account.Users.Add(user);
            return user;
        }

        public Guid SuccessorOfKind(string kind, User target)
        {
            switch (kind)
            {
                case "self":
                    return target.Id;
                case "missing":
                    return Guid.NewGuid();
                case "foreign":
                    var foreign = User.Register(
                        Guid.NewGuid(),
                        "Bo",
                        "Other",
                        "bo@example.com",
                        "hashed:x",
                        UserActorType.TenantEmployee
                    ).Value;
                    Account.Users.Add(foreign);
                    return foreign.Id;
                case "inactive":
                    var inactive = AddEmployee("inactive@example.com");
                    inactive.Deactivate(Guid.NewGuid(), DateTime.UtcNow);
                    return inactive.Id;
                default:
                    var portal = User.Register(
                        Account.Tenant.Id,
                        "Cy",
                        "Client",
                        "cy@example.com",
                        "hashed:x",
                        UserActorType.CustomerPortal,
                        Guid.NewGuid()
                    ).Value;
                    Account.Users.Add(portal);
                    return portal.Id;
            }
        }

        public async Task<Guid> OpenSessionAsync(User user)
        {
            await Account.Issuer.StartSessionAsync(
                user,
                "UTC",
                [],
                ["pwd"],
                null,
                TaxVision.Auth.Domain.RefreshTokens.SessionSurface.Workspace,
                CancellationToken.None
            );
            return Account.Sessions.Sessions.Single(session => session.UserId == user.Id).Id;
        }

        public void AssertUntouched(User target)
        {
            Assert.NotEqual(UserStatus.Offboarded, target.Status);
            Assert.Empty(Runs.Runs);
            Assert.Empty(Account.Audit.Logs);
            Assert.Empty(Bus.Published);
            Assert.Equal(0, UnitOfWork.SaveChangesCallCount);
        }

        public Task<Result> OffboardAsync(
            User target,
            Guid? requestedBy = null,
            Guid? successorId = null,
            OffboardingReason? reason = null,
            string? note = null
        ) =>
            OffboardUserHandler.Handle(
                new OffboardUserCommand(
                    target.TenantId == Account.Tenant.Id ? target.TenantId : Account.Tenant.Id,
                    target.Id,
                    requestedBy ?? Guid.NewGuid(),
                    successorId,
                    reason,
                    note
                ),
                Account.Users,
                new NoTenantAdminLock(),
                new OffboardingHygiene(
                    Mfa,
                    Invitations,
                    new AuthAuditTrail(Account.Audit, Account.Request, Account.Correlation)
                ),
                Runs,
                new SessionAccessCutoff(Account.Sessions, Account.Denylist, Revocations),
                new AuthAuditTrail(Account.Audit, Account.Request, Account.Correlation),
                UnitOfWork,
                Bus,
                CancellationToken.None
            );
    }

    private sealed class ProbeUnitOfWork(FakeMessageBus bus) : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }
        public int PublishedAtLastSave { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            PublishedAtLastSave = bus.Published.Count;
            return Task.FromResult(1);
        }
    }

    private sealed class NoTenantAdminLock : ITenantAdminLock
    {
        public Task AcquireAsync(Guid tenantId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingRevocations : ISessionRevocationPublisher
    {
        public List<Guid> Revoked { get; } = [];

        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        )
        {
            Revoked.Add(sessionId);
            return Task.CompletedTask;
        }
    }
}
