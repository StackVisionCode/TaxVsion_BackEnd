using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Offboarding;
using TaxVision.Auth.Domain.Offboarding;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Offboarding;

/// <summary>Acuse por servicio del retiro: corrida, acuses idempotentes, estado, barrido y aviso.</summary>
public sealed class OffboardingRunTrackingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
    private static readonly (string, string)[] TwoSteps =
    [
        ("Calendar", "CalendarUserOffboardedConsumer"),
        ("Communication", "offboarding-consumers"),
    ];

    private static readonly OffboardingDetails Details = OffboardingDetails
        .Create(Guid.NewGuid(), successorUserId: null, reason: null, reasonNote: null)
        .Value;

    private static OffboardingRun NewRun(Guid? userId = null) =>
        OffboardingRun.Start(TenantId, userId ?? Guid.NewGuid(), Start, Details, TwoSteps).Value;

    // ------------------------------------------------------------ dominio

    [Fact]
    public void A_new_run_expects_every_step_and_is_in_progress()
    {
        var run = NewRun();

        Assert.Equal(2, run.ExpectedCount);
        Assert.Equal(0, run.CompletedCount);
        Assert.Equal(OffboardingRunStatus.InProgress, run.Status);
        Assert.Null(run.CompletedAtUtc);
    }

    [Fact]
    public void Acknowledgements_are_idempotent_and_complete_the_run()
    {
        var run = NewRun();

        Assert.True(run.CompleteStep("Calendar", "CalendarUserOffboardedConsumer", Start.AddMinutes(1)).Value);
        Assert.False(run.CompleteStep("Calendar", "CalendarUserOffboardedConsumer", Start.AddMinutes(9)).Value);
        Assert.True(run.CompleteStep("Communication", "offboarding-consumers", Start.AddMinutes(2)).Value);

        Assert.Equal(OffboardingRunStatus.Completed, run.Status);
        Assert.Equal(Start.AddMinutes(2), run.CompletedAtUtc);
        Assert.Empty(run.PendingSteps);
    }

    [Fact]
    public void An_unexpected_step_is_rejected_without_counting()
    {
        var run = NewRun();

        var result = run.CompleteStep("Ghost", "GhostConsumer", Start);

        Assert.True(result.IsFailure);
        Assert.Equal("Offboarding.UnexpectedStep", result.Error.Code);
        Assert.Equal(0, run.CompletedCount);
    }

    [Fact]
    public void Marking_incomplete_needs_the_timeout_pending_steps_and_happens_once()
    {
        var run = NewRun();
        var timeout = TimeSpan.FromMinutes(30);

        Assert.Equal("Offboarding.NotDue", run.MarkIncomplete(Start.AddMinutes(10), timeout).Error.Code);
        Assert.True(run.MarkIncomplete(Start.AddMinutes(31), timeout).IsSuccess);
        Assert.Equal(OffboardingRunStatus.Incomplete, run.Status);
        Assert.Equal("Offboarding.AlreadyIncomplete", run.MarkIncomplete(Start.AddMinutes(40), timeout).Error.Code);

        // Un acuse tardío lo completa igual.
        run.CompleteStep("Calendar", "CalendarUserOffboardedConsumer", Start.AddMinutes(41));
        run.CompleteStep("Communication", "offboarding-consumers", Start.AddMinutes(42));
        Assert.Equal(OffboardingRunStatus.Completed, run.Status);
    }

    [Fact]
    public void A_completed_run_is_never_marked_incomplete()
    {
        var run = NewRun();
        run.CompleteStep("Calendar", "CalendarUserOffboardedConsumer", Start);
        run.CompleteStep("Communication", "offboarding-consumers", Start);

        Assert.Equal(
            "Offboarding.AlreadyCompleted",
            run.MarkIncomplete(Start.AddHours(2), TimeSpan.FromMinutes(30)).Error.Code
        );
    }

    // ------------------------------------------------------------ registro

    [Fact]
    public void The_registry_has_unique_services_and_steps_and_valid_impact_paths()
    {
        var all = OffboardingParticipants.All;

        Assert.Equal(all.Count, all.Select(p => p.Service).Distinct().Count());
        Assert.All(all, p => Assert.Equal(p.Steps.Count, p.Steps.Distinct().Count()));
        Assert.All(
            all.Where(p => p.ImpactPath is not null),
            p => Assert.Matches(@"^/[a-z]+/offboarding-impact/\{userId\}$", p.ImpactPath!)
        );
        Assert.All(all.Where(p => p.AcceptsEmail), p => Assert.NotNull(p.ImpactPath));
        Assert.Equal("Clients", OffboardingParticipants.ModuleOf("Customer"));
        Assert.Equal("Unknown", OffboardingParticipants.ModuleOf("Unknown"));
    }

    // ------------------------------------------------------------ consumer del acuse

    [Fact]
    public async Task The_consumer_records_an_acknowledgement_once()
    {
        var run = NewRun();
        var runs = new InMemoryOffboardingRunRepository(run);
        var unitOfWork = new CountingUnitOfWork();

        await Acknowledge(runs, unitOfWork, run.UserId, "Calendar", "CalendarUserOffboardedConsumer");
        await Acknowledge(runs, unitOfWork, run.UserId, "Calendar", "CalendarUserOffboardedConsumer");

        Assert.Equal(1, run.CompletedCount);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task The_consumer_ignores_unknown_runs_and_unexpected_steps()
    {
        var run = NewRun();
        var runs = new InMemoryOffboardingRunRepository(run);
        var unitOfWork = new CountingUnitOfWork();

        await Acknowledge(runs, unitOfWork, Guid.NewGuid(), "Calendar", "CalendarUserOffboardedConsumer");
        await Acknowledge(runs, unitOfWork, run.UserId, "Ghost", "GhostConsumer");

        Assert.Equal(0, run.CompletedCount);
        Assert.Equal(0, unitOfWork.Saves);
    }

    // ------------------------------------------------------------ ficha del retiro

    [Fact]
    public void The_record_keeps_who_offboarded_the_successor_and_a_trimmed_optional_reason()
    {
        var actor = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var details = OffboardingDetails.Create(
            actor,
            successor,
            OffboardingReason.Resigned,
            "  Moved to another city  "
        );

        Assert.True(details.IsSuccess);
        var run = OffboardingRun.Start(TenantId, Guid.NewGuid(), Start, details.Value, TwoSteps).Value;
        Assert.Equal(actor, run.OffboardedByUserId);
        Assert.Equal(successor, run.SuccessorUserId);
        Assert.Equal(OffboardingReason.Resigned, run.Reason);
        Assert.Equal("Moved to another city", run.ReasonNote);
    }

    [Fact]
    public void The_reason_is_optional_and_a_blank_note_is_not_stored()
    {
        var details = OffboardingDetails.Create(Guid.NewGuid(), null, null, "   ").Value;

        Assert.Null(details.Reason);
        Assert.Null(details.ReasonNote);
    }

    [Fact]
    public void An_undefined_reason_or_a_note_that_is_too_long_is_rejected()
    {
        Assert.Equal(
            "Offboarding.InvalidReason",
            OffboardingDetails.Create(Guid.NewGuid(), null, (OffboardingReason)99, null).Error.Code
        );
        Assert.Equal(
            "Offboarding.ReasonNoteTooLong",
            OffboardingDetails
                .Create(
                    Guid.NewGuid(),
                    null,
                    OffboardingReason.Other,
                    new string('a', OffboardingDetails.ReasonNoteMaxLength + 1)
                )
                .Error.Code
        );
    }

    [Fact]
    public async Task The_status_shows_the_record_with_display_names()
    {
        var admin = User.Register(
            TenantId,
            "Carlos",
            "Castillo",
            "carlos@office.test",
            "hash",
            UserActorType.TenantAdmin
        ).Value;
        var successor = User.Register(
            TenantId,
            "Manuel",
            "Fedora",
            "manuel@office.test",
            "hash",
            UserActorType.TenantEmployee
        ).Value;
        var leaver = Guid.NewGuid();
        var details = OffboardingDetails
            .Create(admin.Id, successor.Id, OffboardingReason.LetGo, "Contract review")
            .Value;
        var run = OffboardingRun.Start(TenantId, leaver, Start, details, TwoSteps).Value;

        var result = await GetOffboardingStatusHandler.Handle(
            new GetOffboardingStatusQuery(TenantId, leaver),
            new InMemoryOffboardingRunRepository(run),
            UserRepositoryProxy.For(admin, successor),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(new OffboardingPersonResponse(admin.Id, "Carlos Castillo"), result.Value.OffboardedBy);
        Assert.Equal(new OffboardingPersonResponse(successor.Id, "Manuel Fedora"), result.Value.Successor);
        Assert.Equal("LetGo", result.Value.Reason);
        Assert.Equal("Contract review", result.Value.ReasonNote);
    }

    [Fact]
    public async Task Someone_outside_the_office_shows_as_support_and_no_successor_stays_empty()
    {
        var platformAdmin = User.Register(
            Guid.NewGuid(),
            "Pat",
            "Ops",
            "pat@platform.test",
            "hash",
            UserActorType.TenantAdmin
        ).Value;
        var leaver = Guid.NewGuid();
        var details = OffboardingDetails.Create(platformAdmin.Id, null, null, null).Value;
        var run = OffboardingRun.Start(TenantId, leaver, Start, details, TwoSteps).Value;

        var result = await GetOffboardingStatusHandler.Handle(
            new GetOffboardingStatusQuery(TenantId, leaver),
            new InMemoryOffboardingRunRepository(run),
            UserRepositoryProxy.For(platformAdmin),
            CancellationToken.None
        );

        Assert.Equal("Support team", result.Value.OffboardedBy!.Name);
        Assert.Null(result.Value.Successor);
        Assert.Null(result.Value.Reason);
    }

    // ------------------------------------------------------------ marcar incompleta + aviso

    [Fact]
    public async Task Flagging_an_overdue_run_saves_before_publishing_the_notice_with_human_module_names()
    {
        var user = User.Register(
            TenantId,
            "Andres",
            "Hernandez",
            "andres@office.test",
            "hash",
            UserActorType.TenantEmployee
        ).Value;
        var run = OffboardingRun.Start(TenantId, user.Id, DateTime.UtcNow.AddHours(-1), Details, TwoSteps).Value;
        run.CompleteStep("Communication", "offboarding-consumers", DateTime.UtcNow);
        var bus = new FakeMessageBus();
        var unitOfWork = new CountingUnitOfWork(bus);

        var result = await MarkOffboardingRunIncompleteHandler.Handle(
            new MarkOffboardingRunIncompleteCommand(TenantId, run.Id),
            new InMemoryOffboardingRunRepository(run),
            UserRepositoryProxy.For(user),
            Options.Create(new OffboardingTrackingOptions()),
            new FixedCorrelation(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(OffboardingRunStatus.Incomplete, run.Status);
        Assert.Equal(0, unitOfWork.PublishedBeforeSave);
        var notice = Assert.Single(bus.Published.OfType<UserOffboardingIncompleteIntegrationEvent>());
        Assert.Equal("Andres Hernandez", notice.UserDisplayName);
        Assert.Equal(2, notice.ExpectedSteps);
        Assert.Equal(1, notice.CompletedSteps);
        var pending = Assert.Single(notice.PendingSteps);
        Assert.Equal(new PendingOffboardingStep("Calendar", "CalendarUserOffboardedConsumer", "Calendar"), pending);
        Assert.Equal(TenantId, notice.TenantId);
    }

    [Fact]
    public async Task A_run_within_its_time_limit_is_not_flagged_nor_announced()
    {
        var run = OffboardingRun.Start(TenantId, Guid.NewGuid(), DateTime.UtcNow, Details, TwoSteps).Value;
        var bus = new FakeMessageBus();

        var result = await MarkOffboardingRunIncompleteHandler.Handle(
            new MarkOffboardingRunIncompleteCommand(TenantId, run.Id),
            new InMemoryOffboardingRunRepository(run),
            UserRepositoryProxy.For(null),
            Options.Create(new OffboardingTrackingOptions()),
            new FixedCorrelation(),
            new CountingUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Empty(bus.Published);
    }

    [Theory]
    [InlineData(4, 5, 100, false)]
    [InlineData(30, 0, 100, false)]
    [InlineData(30, 5, 0, false)]
    [InlineData(30, 5, 100, true)]
    public void Options_are_validated(int timeout, int interval, int batch, bool valid) =>
        Assert.Equal(
            valid,
            !new OffboardingTrackingOptions
            {
                TimeoutMinutes = timeout,
                IntervalMinutes = interval,
                BatchSize = batch,
            }
                .Errors()
                .Any()
        );

    // ------------------------------------------------------------ persistencia real (EF InMemory)

    [Fact]
    public async Task Runs_and_steps_round_trip_and_acknowledgements_update_only_their_row()
    {
        var database = Guid.NewGuid().ToString();
        var run = NewRun();
        await using (var db = Context(database))
        {
            await new OffboardingRunRepository(db).AddAsync(run, CancellationToken.None);
            Assert.All(db.ChangeTracker.Entries<OffboardingRunStep>(), e => Assert.Equal(EntityState.Added, e.State));
            await db.SaveChangesAsync();
        }

        await using (var db = Context(database))
        {
            var loaded = await new OffboardingRunRepository(db).GetByUserAsync(
                TenantId,
                run.UserId,
                CancellationToken.None
            );
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.ExpectedCount);
            loaded.CompleteStep("Calendar", "CalendarUserOffboardedConsumer", Start);
            await db.SaveChangesAsync();
        }

        await using (var db = Context(database))
        {
            var repository = new OffboardingRunRepository(db);
            var loaded = await repository.GetByUserAsync(TenantId, run.UserId, CancellationToken.None);
            Assert.Equal(1, loaded!.CompletedCount);
            Assert.Null(await repository.GetByUserAsync(Guid.NewGuid(), run.UserId, CancellationToken.None));

            var overdue = await repository.ListOverdueAsync(Start.AddMinutes(31), 10, CancellationToken.None);
            Assert.Equal(new OverdueOffboardingRun(TenantId, run.Id), Assert.Single(overdue));
            Assert.Empty(await repository.ListOverdueAsync(Start, 10, CancellationToken.None));
        }
    }

    // ------------------------------------------------------------ dobles

    private static Task Acknowledge(
        IOffboardingRunRepository runs,
        IUnitOfWork unitOfWork,
        Guid userId,
        string service,
        string step
    ) =>
        OffboardingStepCompletedConsumer.Handle(
            new UserOffboardingStepCompletedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Service = service,
                Step = step,
                CompletedAtUtc = Start,
            },
            runs,
            unitOfWork,
            NullLogger<OffboardingRun>.Instance,
            CancellationToken.None
        );

    private static AuthDbContext Context(string database) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(database).Options, new NoTenant());

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private sealed class CountingUnitOfWork(FakeMessageBus? bus = null) : IUnitOfWork
    {
        public int Saves { get; private set; }

        /// <summary>Mensajes ya publicados cuando se guardó por primera vez.</summary>
        public int PublishedBeforeSave { get; private set; } = -1;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            if (Saves == 0)
                PublishedBeforeSave = bus?.Published.Count ?? 0;
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "corr-offboarding";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }

    /// <summary>Solo responde GetByIdAsync; el resto no lo usa el handler.</summary>
    public class UserRepositoryProxy : DispatchProxy
    {
        private User?[] _users = [];

        public static IUserRepository For(params User?[] users)
        {
            var proxy = Create<IUserRepository, UserRepositoryProxy>();
            ((UserRepositoryProxy)(object)proxy)._users = users;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IUserRepository.GetByIdAsync)
                ? Task.FromResult(_users.FirstOrDefault(u => u is not null && Equals(args![0], u.Id)))
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
