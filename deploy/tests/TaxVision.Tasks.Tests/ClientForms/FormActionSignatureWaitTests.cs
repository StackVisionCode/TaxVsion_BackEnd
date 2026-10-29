using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Actions.Consumers;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using Wolverine;
using static TaxVision.Tasks.Tests.ClientForms.FormActionTestDoubles;
using FormActionTrigger = TaxVision.DataCollection.Engine.Definitions.FormActionTrigger;

namespace TaxVision.Tasks.Tests.ClientForms;

// CreateCustomer con config.waitFor = "signatureCompleted": no corre al enviar, corre cuando la firma
// de la revisión se completa, y queda Skipped(signature_not_completed) si la firma no se completa.
public sealed class FormActionSignatureWaitTests
{
    private const string SignatureStep =
        ",{\"id\":\"fd_sign00001\",\"type\":\"signatureStep\",\"props\":{\"documentSource\":\"submissionSummary\",\"verification\":\"emailOtp\"}}";

    [Theory]
    [InlineData(FormActionTrigger.OnSubmit, "onSubmit")]
    [InlineData(FormActionTrigger.OnComplete, "onComplete")]
    public async Task The_action_waits_instead_of_running_when_its_trigger_fires(FormActionTrigger trigger, string json)
    {
        var (request, planner) = Submitted(json);
        var outgoing = new DeferredMessages();

        await planner.PlanAsync(request, trigger, LatestAnswers(), outgoing, Now, default);

        Assert.Empty(request.ActionRuns);
        Assert.Empty(outgoing.Messages);
    }

    [Fact]
    public async Task Without_a_signature_step_the_wait_is_skipped_as_not_configured()
    {
        var (request, planner) = Submitted(withSignatureStep: false);

        await planner.PlanAsync(
            request,
            FormActionTrigger.OnSubmit,
            LatestAnswers(),
            new DeferredMessages(),
            Now,
            default
        );

        var run = Assert.Single(request.ActionRuns);
        Assert.Equal(FormActionRunStatus.Skipped, run.Status);
        Assert.Equal(FormActionRunErrors.SignatureNotConfiguredCode, run.ErrorCode);
    }

    [Fact]
    public async Task The_customer_is_created_when_the_signature_completes_and_only_after_saving()
    {
        var (request, planner) = Waiting();
        var (bus, recorder) = OutboxMessageBus.Create();

        await CompletedAsync(request, planner, bus, new OutboxUnitOfWork(recorder));

        var run = Assert.Single(request.ActionRuns);
        Assert.Equal(FormActionRunStatus.Requested, run.Status);
        // Publicado después de guardar: nada salió con el commit, el comando queda tras él.
        Assert.Empty(recorder.Flushed);
        var command = Assert.IsType<CustomerCreationFromFormRequestedIntegrationEvent>(Assert.Single(recorder.Pending));
        Assert.Equal(run.CommandId, command.RequestId);
    }

    [Fact]
    public async Task A_failed_save_on_completion_publishes_no_command()
    {
        var (request, planner) = Waiting();
        var (bus, recorder) = OutboxMessageBus.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CompletedAsync(request, planner, bus, new OutboxUnitOfWork(recorder, new InvalidOperationException("save")))
        );

        Assert.Empty(recorder.Pending);
        Assert.Empty(recorder.Flushed);
    }

    [Fact]
    public async Task Repeated_completion_and_sealing_events_create_the_customer_once()
    {
        var (request, planner) = Waiting();
        var (bus, recorder) = OutboxMessageBus.Create();
        var unitOfWork = new OutboxUnitOfWork(recorder);

        await CompletedAsync(request, planner, bus, unitOfWork);
        await CompletedAsync(request, planner, bus, unitOfWork);
        await SealedAsync(request, planner, bus, unitOfWork);

        Assert.Single(request.ActionRuns);
        Assert.Single(Published(recorder).OfType<CustomerCreationFromFormRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task A_sealed_event_alone_also_runs_the_waiting_action()
    {
        var (request, planner) = Waiting();
        var (bus, recorder) = OutboxMessageBus.Create();

        await SealedAsync(request, planner, bus, new OutboxUnitOfWork(recorder));

        Assert.Equal(FormActionRunStatus.Requested, Assert.Single(request.ActionRuns).Status);
    }

    [Fact]
    public async Task A_declined_signature_skips_the_action_once_and_warns_the_responsible()
    {
        var (request, planner) = Waiting();
        var (bus, recorder) = OutboxMessageBus.Create();
        var unitOfWork = new OutboxUnitOfWork(recorder);

        await DeclinedAsync(request, planner, bus, unitOfWork);
        await DeclinedAsync(request, planner, bus, unitOfWork);

        var run = Assert.Single(request.ActionRuns);
        Assert.Equal(FormActionRunStatus.Skipped, run.Status);
        Assert.Equal(FormActionRunErrors.SignatureNotCompletedCode, run.ErrorCode);
        Assert.Null(run.CommandId);
        Assert.Empty(Published(recorder).OfType<CustomerCreationFromFormRequestedIntegrationEvent>());
        Assert.Contains(Published(recorder), message => message is FormActionFailedIntegrationEvent);
    }

    [Fact]
    public async Task An_expired_signature_skips_the_action()
    {
        var (request, planner) = Waiting();
        var (bus, recorder) = OutboxMessageBus.Create();

        await FormSignatureExpiredConsumer.Handle(
            new SignatureRequestExpiredIntegrationEvent
            {
                TenantId = TenantId,
                SignatureRequestId = Guid.NewGuid(),
                CreatedByUserId = Responsible,
                ExpiredAtUtc = Now,
                RevocationEpoch = 1,
                PendingSignerIds = [],
                PendingSigners = [],
                ExternalReference = Reference(request),
            },
            new ExtRequests(request),
            planner,
            new OutboxUnitOfWork(recorder),
            bus,
            new TestCorrelationContext(),
            NullLogger<SignatureRequestExpiredIntegrationEvent>.Instance,
            default
        );

        var run = Assert.Single(request.ActionRuns);
        Assert.Equal(FormActionRunErrors.SignatureNotCompletedCode, run.ErrorCode);
    }

    [Fact]
    public async Task A_late_decline_after_completion_does_not_undo_the_creation()
    {
        var (request, planner) = Waiting();
        var (bus, recorder) = OutboxMessageBus.Create();
        var unitOfWork = new OutboxUnitOfWork(recorder);

        await CompletedAsync(request, planner, bus, unitOfWork);
        await DeclinedAsync(request, planner, bus, unitOfWork);

        Assert.Equal(FormActionRunStatus.Requested, Assert.Single(request.ActionRuns).Status);
    }

    // Enviada y planificada (esperando); la firma de la revisión 1 está en curso.
    private static (FormRequest Request, FormActionPlanner Planner) Waiting()
    {
        var (request, planner) = Submitted();
        planner
            .PlanAsync(request, FormActionTrigger.OnSubmit, LatestAnswers(), new DeferredMessages(), Now, default)
            .GetAwaiter()
            .GetResult();
        Assert.True(request.MarkSignatureRequesting(1, Guid.NewGuid(), new string('b', 64)).IsSuccess);
        Assert.True(request.LinkSignatureRequest(1, Guid.NewGuid()).IsSuccess);
        Assert.Empty(request.ActionRuns);
        return (request, planner);
    }

    private static (FormRequest Request, FormActionPlanner Planner) Submitted(
        string trigger = "onSubmit",
        bool withSignatureStep = true
    )
    {
        var version = SignedVersion(trigger, withSignatureStep);
        var participant = Participant();
        var request = ExternalRequest(version, participant);
        Submit(request, participant);
        return (request, Planner(new MemoryFormTemplateVersionRepository(version)));
    }

    private static TaxVision.DataCollection.Engine.Answers.FormAnswers LatestAnswers() =>
        FormTemplateHandlerTestDoubles.Engine().ParseAnswers(Answers).Value;

    private static FormTemplateVersion SignedVersion(string trigger, bool withSignatureStep)
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var json = FormMappingTestDefinitions.Onboarding;
        if (withSignatureStep)
            json = json.Replace(
                "\"props\":{\"country\":\"US\"}}]",
                "\"props\":{\"country\":\"US\"}}" + SignatureStep + "]"
            );
        var start = json.IndexOf("\"actions\":", StringComparison.Ordinal);
        json =
            json[..start]
            + "\"actions\":[{\"key\":\"create_client\",\"type\":\"createCustomer\",\"trigger\":\""
            + trigger
            + "\",\"config\":{\"kind\":{\"constant\":\"Individual\"},\"language\":{\"mapped\":true},\"waitFor\":\"signatureCompleted\"}}]}";
        var definition = engine.ParseDefinition(json).Value;
        var canonical = engine.Canonicalize(definition);
        var rules = engine.CanonicalizeRuleSets(definition);
        return FormTemplateVersion.Create(
            TenantId,
            Guid.NewGuid(),
            1,
            "Client onboarding",
            FormPurpose.ClientIntake,
            FormPresentation.Wizard,
            ClientFormsTestDocuments.Definition(canonical.Json),
            ClientFormsTestDocuments.Hash(canonical.Sha256),
            8,
            Responsible,
            Now,
            FormVersionRuleSets.Create(rules.MappingSetJson, rules.ActionSetJson, 1).Value
        );
    }

    private static IEnumerable<object> Published(OutboxMessageBus recorder) =>
        recorder.Flushed.Concat(recorder.Pending);

    private static SignatureExternalReference Reference(FormRequest request) =>
        new()
        {
            SourceService = ClientFormSignatureReference.SourceService,
            SourceType = ClientFormSignatureReference.SourceType,
            SourceId = ClientFormSignatureReference.BuildSourceId(request.Id, 1),
        };

    private static Task CompletedAsync(
        FormRequest request,
        FormActionPlanner planner,
        IMessageBus bus,
        BuildingBlocks.Persistence.IUnitOfWork unitOfWork
    ) =>
        FormSignatureCompletedConsumer.Handle(
            new SignatureRequestCompletedIntegrationEvent
            {
                TenantId = TenantId,
                SignatureRequestId = Guid.NewGuid(),
                CreatedByUserId = Responsible,
                CompletedAtUtc = Now,
                Documents = [],
                SignerIds = [],
                GenerateCertificate = false,
                Signers = [],
                ExternalReference = Reference(request),
            },
            new ExtRequests(request),
            planner,
            unitOfWork,
            bus,
            new TestCorrelationContext(),
            NullLogger<SignatureRequestCompletedIntegrationEvent>.Instance,
            default
        );

    private static Task SealedAsync(
        FormRequest request,
        FormActionPlanner planner,
        IMessageBus bus,
        BuildingBlocks.Persistence.IUnitOfWork unitOfWork
    ) =>
        FormSignatureDocumentSealedConsumer.Handle(
            new SignatureDocumentSealedIntegrationEvent
            {
                TenantId = TenantId,
                SignatureRequestId = Guid.NewGuid(),
                CreatedByUserId = Responsible,
                IdempotencyKey = "sealed",
                DocumentId = Guid.NewGuid(),
                SealedFileId = Guid.NewGuid(),
                DocumentHashPost = new string('c', 64),
                SealedAtUtc = Now,
                ExternalReference = Reference(request),
            },
            new ExtRequests(request),
            planner,
            unitOfWork,
            bus,
            new TestCorrelationContext(),
            NullLogger<SignatureDocumentSealedIntegrationEvent>.Instance,
            default
        );

    private static Task DeclinedAsync(
        FormRequest request,
        FormActionPlanner planner,
        IMessageBus bus,
        BuildingBlocks.Persistence.IUnitOfWork unitOfWork
    ) =>
        FormSignatureRejectedConsumer.Handle(
            new SignerRejectedIntegrationEvent
            {
                TenantId = TenantId,
                SignatureRequestId = Guid.NewGuid(),
                CreatedByUserId = Responsible,
                SignerId = Guid.NewGuid(),
                RejectedAtUtc = Now,
                RevocationEpoch = 1,
                PendingSignerIds = [],
                PendingSigners = [],
                ExternalReference = Reference(request),
            },
            new ExtRequests(request),
            planner,
            unitOfWork,
            bus,
            new TestCorrelationContext(),
            NullLogger<SignerRejectedIntegrationEvent>.Instance,
            default
        );
}
