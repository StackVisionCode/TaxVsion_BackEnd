using System.Text.Json;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.ClientForms;

// Enviar como externo a un email que ya es de un cliente no archivado se rechaza en el backend.
public sealed class AssignExternalFormRequestHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-0000000000e1");
    private static readonly Guid CustomerId = Guid.Parse("30000000-0000-0000-0000-0000000000e1");
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Email_of_a_visible_client_is_rejected_with_the_client_id()
    {
        var fixture = Fixture();
        var bus = new FakeMessageBus();

        var outcome = await AssignAsync(fixture, new ExtCustomerEmails(CustomerId), new ExtVisibility(true), bus);

        Assert.True(outcome.Result.IsFailure);
        Assert.Equal(FormParticipantErrors.EmailBelongsToCustomer, outcome.Result.Error);
        Assert.Equal(CustomerId, outcome.ExistingCustomerId);
        Assert.Empty(fixture.Requests.Items);
        Assert.Empty(fixture.Participants.Items);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Email_of_a_client_the_sender_cannot_see_is_still_rejected_but_without_the_client_id()
    {
        var fixture = Fixture();

        var outcome = await AssignAsync(fixture, new ExtCustomerEmails(CustomerId), new ExtVisibility(false));

        Assert.Equal(FormParticipantErrors.EmailBelongsToCustomer, outcome.Result.Error);
        Assert.Null(outcome.ExistingCustomerId);
        Assert.Empty(fixture.Requests.Items);
    }

    [Fact]
    public void The_conflict_body_omits_the_client_id_when_it_is_not_visible()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var error = FormParticipantErrors.EmailBelongsToCustomer;

        var hidden = JsonSerializer.Serialize(
            new ExternalCustomerConflictResponse(error.Code, error.Message, null),
            options
        );
        var visible = JsonSerializer.Serialize(
            new ExternalCustomerConflictResponse(error.Code, error.Message, CustomerId),
            options
        );

        Assert.Equal(
            "{\"code\":\"FormParticipant.EmailBelongsToCustomer\",\"message\":\"This email belongs to an existing client. Assign the form to the client instead.\"}",
            hidden
        );
        Assert.Contains($"\"customerId\":\"{CustomerId}\"", visible, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_of_an_archived_client_can_still_receive_an_external_form()
    {
        await using var db = NewContext();
        db.CustomerDirectoryEntries.Add(
            CustomerDirectoryEntry.Create(
                FormActionTestDoubles.TenantId,
                CustomerId,
                "Ada",
                CustomerDirectoryStatus.Archived,
                Now,
                "ada@example.com"
            )
        );
        await db.SaveChangesAsync();
        var fixture = Fixture();

        var outcome = await AssignAsync(fixture, EmailDirectory(db), new ExtVisibility(true));

        Assert.True(outcome.Result.IsSuccess);
        Assert.Single(fixture.Requests.Items);
        Assert.Equal("ada@example.com", Assert.Single(fixture.Participants.Items).Email);
    }

    [Theory]
    [InlineData(CustomerDirectoryStatus.Active)]
    [InlineData(CustomerDirectoryStatus.Inactive)]
    public async Task The_match_is_case_insensitive_and_covers_every_non_archived_client(CustomerDirectoryStatus status)
    {
        await using var db = NewContext();
        // El evento de Customer trae el email tal como lo escribieron; la proyección lo normaliza.
        db.CustomerDirectoryEntries.Add(
            CustomerDirectoryEntry.Create(
                FormActionTestDoubles.TenantId,
                CustomerId,
                "Ada",
                status,
                Now,
                "  Ada@Example.COM "
            )
        );
        await db.SaveChangesAsync();
        var fixture = Fixture();

        var outcome = await AssignAsync(
            fixture,
            EmailDirectory(db),
            new ExtVisibility(true),
            email: " ADA@example.com "
        );

        Assert.Equal(FormParticipantErrors.EmailBelongsToCustomer, outcome.Result.Error);
        Assert.Equal(CustomerId, outcome.ExistingCustomerId);
    }

    [Fact]
    public async Task A_customer_of_another_tenant_does_not_block_the_send()
    {
        await using var db = NewContext();
        db.CustomerDirectoryEntries.Add(
            CustomerDirectoryEntry.Create(
                Guid.NewGuid(),
                CustomerId,
                "Ada",
                CustomerDirectoryStatus.Active,
                Now,
                "ada@example.com"
            )
        );
        await db.SaveChangesAsync();
        var fixture = Fixture();

        var outcome = await AssignAsync(fixture, EmailDirectory(db), new ExtVisibility(true));

        Assert.True(outcome.Result.IsSuccess);
    }

    [Fact]
    public void A_newer_customer_event_without_email_keeps_the_projected_email()
    {
        var entry = CustomerDirectoryEntry.Create(
            FormActionTestDoubles.TenantId,
            CustomerId,
            "Ada",
            CustomerDirectoryStatus.Active,
            Now,
            "Ada@Example.com"
        );

        entry.ApplyIfNewer(null, CustomerDirectoryStatus.Inactive, Now.AddMinutes(1));
        Assert.Equal("ada@example.com", entry.PrimaryEmailNormalized);
        entry.ApplyIfNewer("Ada", CustomerDirectoryStatus.Active, Now.AddMinutes(2), "New@Example.com");
        Assert.Equal("new@example.com", entry.PrimaryEmailNormalized);
    }

    private static Task<AssignExternalFormRequestOutcome> AssignAsync(
        FixtureState fixture,
        IClientFormsCustomerEmailDirectory emails,
        IFormRequestVisibilityFilter visibility,
        FakeMessageBus? bus = null,
        string email = "ada@example.com"
    ) =>
        AssignExternalFormRequestHandler.Handle(
            new AssignExternalFormRequestCommand(
                FormActionTestDoubles.TenantId,
                UserId,
                email,
                "Ada",
                fixture.Template.Id,
                "identity_verification",
                null,
                null,
                null,
                null,
                Now,
                CanViewAll: false
            ),
            emails,
            visibility,
            fixture.Participants,
            new ExtGrants(),
            new ExtCapabilities(),
            new ExtSecurity(),
            new MemoryFormTemplateRepository(fixture.Template),
            new MemoryFormTemplateVersionRepository(fixture.Version),
            new MemoryConsentNoticeRepository(
                FormTemplateHandlerTestDoubles.IdentityNotice(FormActionTestDoubles.TenantId, UserId, Now)
            ),
            fixture.Requests,
            new FormActionAuthorizer(
                FormTemplateHandlerTestDoubles.Engine(),
                FormTemplateHandlerTestDoubles.TargetCatalog(),
                new GrantedResponsibleUsers()
            ),
            new ExtCodec(),
            new ExtAuditLog(),
            new RecordingUnitOfWork(),
            bus ?? new FakeMessageBus(),
            new TestCorrelationContext(),
            new RecordingClientFormsMetrics(),
            default
        );

    private static FixtureState Fixture()
    {
        var template = FormTemplateHandlerTestDoubles.Template(FormActionTestDoubles.TenantId);
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var parsed = engine.ParseDefinition(template.Draft.Json).Value;
        var version = template
            .Publish(engine.Canonicalize(parsed), parsed.Title, FormPresentation.Wizard, 1, UserId, Now)
            .Value;
        return new FixtureState(template, version, new ExtParticipants(), new ExtRequests());
    }

    private static IClientFormsCustomerEmailDirectory EmailDirectory(TasksDbContext db) =>
        (IClientFormsCustomerEmailDirectory)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.ClientFormsCustomerEmailDirectory",
                    throwOnError: true
                )!,
                db
            )!;

    private static TasksDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private sealed record FixtureState(
        FormTemplate Template,
        FormTemplateVersion Version,
        ExtParticipants Participants,
        ExtRequests Requests
    );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
