using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using Wolverine;
using static TaxVision.Tasks.Tests.ClientForms.FormActionTestDoubles;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormActionPlannerTests
{
    [Fact]
    public async Task Submit_plans_create_customer_and_emits_one_protected_command()
    {
        var (request, version, bus) = await PlanAsync();

        var run = Assert.Single(request.ActionRuns);
        var command = Assert.IsType<CustomerCreationFromFormRequestedIntegrationEvent>(Assert.Single(bus.Messages));
        Assert.Equal(FormActionRunStatus.Requested, run.Status);
        Assert.Equal(run.CommandId, command.RequestId);
        Assert.Equal(run.IdempotencyKey, command.IdempotencyKey);
        Assert.Equal(request.Id, command.SourceReferenceId);
        Assert.Equal(Responsible, command.RequestedByUserId);
        Assert.Equal("tasks.client_forms", command.SourceService);
        Assert.Equal(version.TargetCatalogVersion, command.TargetCatalogVersion);
        Assert.Equal("External", command.Participant.Kind);
        Assert.DoesNotContain("Lovelace", command.Payload.Cipher, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_payload_carries_only_create_customer_targets_with_transforms_and_constants()
    {
        var (request, _, bus) = await PlanAsync();
        var command = (CustomerCreationFromFormRequestedIntegrationEvent)bus.Messages.Single();

        var values = Unprotect(command, request).Values.ToDictionary(item => item.TargetKey);

        Assert.Equal("Ada", values["customer.person.first_name"].Value);
        Assert.Equal("ada@example.com", values["customer.primary_email"].Value);
        Assert.Equal("En", values["customer.language"].Value);
        Assert.Equal("Individual", values["customer.kind"].Value);
        Assert.Equal("1985-12-10", values["customer.date_of_birth"].Value);
        // Sin postCreate primaryAddress la dirección mapeada no viaja.
        Assert.DoesNotContain("customer.address.home", values.Keys);
        Assert.All(values.Values, item => Assert.Equal(1, item.TargetSchemaVersion));
    }

    [Fact]
    public async Task The_mapped_address_travels_only_when_the_action_asks_for_it()
    {
        var (request, _, bus) = await PlanAsync(
            """[{"key":"create_client","type":"createCustomer","trigger":"onSubmit","config":{"kind":{"constant":"Individual"},"language":{"mapped":true},"postCreate":["primaryAddress"]}}]"""
        );
        var command = (CustomerCreationFromFormRequestedIntegrationEvent)bus.Messages.Single();

        var address = Unprotect(command, request)
            .Values.Single(item => item.TargetKey == "customer.address.home")
            .Address!;

        Assert.Equal(new ClientFormAddressValue("1 Main St", null, "Austin", "TX", "78701", "US"), address);
    }

    [Fact]
    public async Task A_false_condition_records_a_skipped_run_without_a_command()
    {
        var (request, _, bus) = await PlanAsync(
            """[{"key":"create_client","type":"createCustomer","trigger":"onSubmit","condition":{"field":"fd_lang00001","op":"equals","value":"spanish"},"config":{"kind":{"constant":"Individual"},"language":{"mapped":true}}}]"""
        );

        var run = Assert.Single(request.ActionRuns);
        Assert.Equal(FormActionRunStatus.Skipped, run.Status);
        Assert.Equal(FormActionRunErrors.ConditionNotMetCode, run.ErrorCode);
        Assert.Null(run.CommandId);
        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task With_automatic_creation_off_the_action_stays_manual()
    {
        var (request, _, bus) = await PlanAsync(autoCreate: false);

        Assert.Empty(request.ActionRuns);
        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task Office_without_settings_row_creates_the_customer_by_default()
    {
        var (request, _, bus) = await PlanAsync(autoCreate: null);

        Assert.Single(request.ActionRuns);
        Assert.IsType<CustomerCreationFromFormRequestedIntegrationEvent>(Assert.Single(bus.Messages));
    }

    [Fact]
    public async Task A_manual_trigger_is_not_planned_on_submit()
    {
        var (request, _, bus) = await PlanAsync(
            """[{"key":"create_client","type":"createCustomer","trigger":"manual","config":{"kind":{"constant":"Individual"},"language":{"mapped":true}}}]"""
        );

        Assert.Empty(request.ActionRuns);
        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task A_resubmission_does_not_create_a_second_customer()
    {
        var version = Version();
        var participant = Participant();
        var request = ExternalRequest(version, participant);
        var planner = Planner(new MemoryFormTemplateVersionRepository(version));
        var bus = new DeferredMessages();
        var answers = Submit(request, participant);
        await planner.PlanAsync(request, FormActionTrigger.OnSubmit, answers, bus, Now, default);
        request.ReturnForChanges("Please fix.", [], Responsible, Now);
        answers = Submit(request, participant);

        await planner.PlanAsync(request, FormActionTrigger.OnSubmit, answers, bus, Now, default);

        Assert.Single(request.ActionRuns);
        Assert.Single(bus.Messages);
    }

    [Fact]
    public async Task Actions_of_later_phases_are_recorded_as_skipped_not_supported_on_complete()
    {
        var version = Version(
            """[{"key":"summary","type":"generateSummary","trigger":"onComplete"},{"key":"retention","type":"applyRetentionClass","trigger":"onComplete"}]"""
        );
        var participant = Participant();
        var request = ExternalRequest(version, participant);
        Submit(request, participant);
        var bus = new DeferredMessages();

        await Planner(new MemoryFormTemplateVersionRepository(version))
            .PlanAsync(request, FormActionTrigger.OnComplete, null, bus, Now, default);

        Assert.Equal(2, request.ActionRuns.Count);
        Assert.All(
            request.ActionRuns,
            run =>
            {
                Assert.Equal(FormActionRunStatus.Skipped, run.Status);
                Assert.Equal(FormActionRunErrors.NotSupportedCode, run.ErrorCode);
            }
        );
        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task A_payload_that_cannot_be_built_fails_the_run_and_warns_the_responsible()
    {
        var bad = Answers.Replace("\"english\"", "\"klingon\"", StringComparison.Ordinal);
        var (request, _, bus) = await PlanAsync(answers: bad);

        var run = Assert.Single(request.ActionRuns);
        Assert.Equal(FormActionRunStatus.Failed, run.Status);
        Assert.Equal(FormActionRunErrors.PayloadUnavailableCode, run.ErrorCode);
        Assert.True(run.IsRetryable);
        var failed = Assert.IsType<FormActionFailedIntegrationEvent>(Assert.Single(bus.Messages));
        Assert.Equal(Responsible, failed.ResponsibleUserId);
    }

    [Fact]
    public async Task A_customer_request_never_plans_customer_creation()
    {
        var version = Version();
        var request = FormRequest
            .Create(
                TenantId,
                FormParticipantRef.Customer(Guid.NewGuid()),
                version,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                null,
                Responsible,
                Now
            )
            .Value;
        request.Submit(new ProtectedAnswers(Answers, 1), [], [], FormActor.PortalUser(Guid.NewGuid()), Now);
        var bus = new DeferredMessages();

        await Planner(new MemoryFormTemplateVersionRepository(version))
            .PlanAsync(
                request,
                FormActionTrigger.OnSubmit,
                FormTemplateHandlerTestDoubles.Engine().ParseAnswers(Answers).Value,
                bus,
                Now,
                default
            );

        Assert.Empty(request.ActionRuns);
        Assert.Empty(bus.Messages);
    }

    // La atomicidad depende de publicar con el bus del handler: el planificador no puede tener uno propio.
    [Fact]
    public void The_planner_has_no_bus_of_its_own()
    {
        var parameters = typeof(FormActionPlanner).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parameters, parameter => typeof(IMessageBus).IsAssignableFrom(parameter.ParameterType));
    }

    // El comando va punto a punto a su único consumidor; la reasignación de archivos, por cola local durable.
    [Fact]
    public void The_command_routes_point_to_point_and_the_reassignment_stays_local_and_durable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;
        var program = string.Concat(
            File.ReadAllText(
                    Path.Combine(directory!.FullName, "src", "Services", "Tasks", "TaxVision.Tasks.Api", "Program.cs")
                )
                .Where(character => !char.IsWhiteSpace(character))
        );

        Assert.Contains(
            "options.PublishMessage<CustomerCreationFromFormRequestedIntegrationEvent>().ToRabbitQueue(\"customer-client-form-commands\");",
            program,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"LocalQueueFor<{typeof(ReassignParticipantFilesRequested).FullName}>().UseDurableInbox()",
            program,
            StringComparison.Ordinal
        );
    }

    internal static CustomerCreationFromFormPayload Unprotect(
        CustomerCreationFromFormRequestedIntegrationEvent command,
        FormRequest request
    ) =>
        Codec.Unprotect<CustomerCreationFromFormPayload>(
            command.Payload,
            ClientFormProtectedPayloadSchemas.CustomerCreation,
            new ProtectedPayloadBinding(
                request.TenantId,
                request.Id,
                command.Participant,
                IdempotencyKey: command.IdempotencyKey
            )
        );

    private static async Task<(
        FormRequest Request,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion Version,
        DeferredMessages Bus
    )> PlanAsync(string? actions = null, bool? autoCreate = true, string answers = Answers)
    {
        var version = Version(actions);
        var participant = Participant();
        var request = ExternalRequest(version, participant);
        var parsed = Submit(request, participant, answers);
        var bus = new DeferredMessages();
        await Planner(new MemoryFormTemplateVersionRepository(version), autoCreate: autoCreate)
            .PlanAsync(request, FormActionTrigger.OnSubmit, parsed, bus, Now, default);
        return (request, version, bus);
    }
}
