using System.Reflection;
using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Settings;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using Wolverine;

namespace TaxVision.Tasks.Tests.ClientForms;

internal static class FormActionTestDoubles
{
    internal static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-0000000000b7");
    internal static readonly Guid Responsible = Guid.Parse("20000000-0000-0000-0000-0000000000b7");
    internal static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    internal const string Answers = """
        {"schemaVersion":1,"values":{"fd_first0001":"  Ada ","fd_last00001":"Lovelace","fd_email0001":"ADA@Example.com","fd_phone0001":"+18095551234","fd_dob000001":"1985-12-10","fd_lang00001":"english","fd_addr00001":{"line1":"1 Main St","city":"Austin","state":"TX","postalCode":"78701","country":"US"}}}
        """;

    internal static JsonProtectedPayloadCodec Codec { get; } =
        new(
            new AesGcmRotatingSecretProtector(
                Options.Create(
                    new RotatingSecretProtectionOptions
                    {
                        MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                        MasterKeyVersion = 1,
                    }
                )
            )
        );

    internal static FormActionPlanner Planner(
        IFormTemplateVersionRepository versions,
        IFormEngine? engine = null,
        bool? autoCreate = true
    ) =>
        new(
            versions,
            engine ?? FormTemplateHandlerTestDoubles.Engine(),
            FormTemplateHandlerTestDoubles.TargetCatalog(),
            new MemoryModuleSettings(autoCreate),
            new PlainFormAnswersProtector(),
            Codec,
            new TestCorrelationContext()
        );

    // La definición de onboarding con su acción; actionsJson reemplaza la lista de acciones.
    internal static FormTemplateVersion Version(string? actionsJson = null)
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var json = FormMappingTestDefinitions.Onboarding;
        if (actionsJson is not null)
        {
            var start = json.IndexOf("\"actions\":", StringComparison.Ordinal);
            json = json[..start] + "\"actions\":" + actionsJson + "}";
        }
        var definition = engine.ParseDefinition(json).Value;
        var canonical = engine.Canonicalize(definition);
        var rules = engine.CanonicalizeRuleSets(definition);
        var ruleSets = FormVersionRuleSets.Create(rules.MappingSetJson, rules.ActionSetJson, 1).Value;
        return FormTemplateVersion.Create(
            TenantId,
            Guid.NewGuid(),
            1,
            "Client onboarding",
            FormPurpose.ClientIntake,
            FormPresentation.Wizard,
            ClientFormsTestDocuments.Definition(canonical.Json),
            ClientFormsTestDocuments.Hash(canonical.Sha256),
            7,
            Responsible,
            Now,
            ruleSets
        );
    }

    internal static FormParticipant Participant()
    {
        var participant = FormParticipant
            .Create(TenantId, "ada@example.com", "Ada", null, FormParticipantSource.DirectedRequest, Now)
            .Value;
        participant.MarkVerified(Now);
        return participant;
    }

    internal static FormRequest ExternalRequest(FormTemplateVersion version, FormParticipant participant) =>
        FormRequest
            .Create(
                TenantId,
                FormParticipantRef.External(participant.Id),
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
                Responsible,
                Responsible,
                Now
            )
            .Value;

    internal static FormActor Actor(FormParticipant participant) =>
        FormActor.ExternalSession(participant.Id, Guid.NewGuid());

    // Envía la revisión con las respuestas dadas y devuelve las respuestas ya leídas por el engine.
    internal static TaxVision.DataCollection.Engine.Answers.FormAnswers Submit(
        FormRequest request,
        FormParticipant participant,
        string answers = Answers
    )
    {
        request.Submit(new ProtectedAnswers(answers, 1), [], [], Actor(participant), Now);
        return FormTemplateHandlerTestDoubles.Engine().ParseAnswers(answers).Value;
    }

    // Antes del envío: un adjunto solo se registra mientras la request es editable.
    internal static Guid AddAttachment(FormRequest request)
    {
        var fileId = Guid.NewGuid();
        request.RegisterAttachment(
            new FormAnswerAddress("fd_first0001", null),
            fileId,
            "id.pdf",
            "application/pdf",
            10,
            5,
            5,
            null,
            Now
        );
        return fileId;
    }
}

// autoCustomerCreation null: la oficina no tiene fila de settings.
internal sealed class MemoryModuleSettings(bool? autoCustomerCreation) : IFormModuleSettingsRepository
{
    public Task<FormModuleSettings?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (autoCustomerCreation is not { } autoCreate)
            return Task.FromResult<FormModuleSettings?>(null);
        var settings = FormModuleSettings.Create(tenantId, null, FormActionTestDoubles.Responsible).Value;
        settings.SetCapabilities(true, true, autoCreate, false, false, FormActionTestDoubles.Responsible);
        return Task.FromResult<FormModuleSettings?>(settings);
    }

    public Task<IReadOnlyList<FormModuleSettings>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FormModuleSettings>>([]);

    public void Add(FormModuleSettings settings) { }
}

// Repositorio de una request con la request abierta en conflicto configurable.
internal sealed class ActionRequestRepository(FormRequest request) : IFormRequestRepository
{
    private readonly SingleFormRequestRepository _inner = new(request);

    internal Guid? OpenConflict { get; set; }

    public void Add(FormRequest item) { }

    public Task<Result<FormRequest>> GetForCommandAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    ) => _inner.GetForCommandAsync(tenantId, requestId, ct);

    public Task<Result<FormRequest>> GetForReviewAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
        _inner.GetForCommandAsync(tenantId, requestId, ct);

    public Task<Guid?> FindOpenRequestIdAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid exceptRequestId,
        CancellationToken ct = default
    ) => Task.FromResult(OpenConflict);

    public Task<bool> HasOpenRequestAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid? exceptRequestId = null,
        CancellationToken ct = default
    ) => Task.FromResult(OpenConflict.HasValue);

    public Task<bool> HasOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    ) => Task.FromResult(false);

    public Task<IReadOnlyList<FormRequest>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

    public Task<IReadOnlyList<FormRequest>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

    public Task<IReadOnlyList<FormRequest>> ListByAttachmentFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FormRequest>>([]);
}

internal sealed class ActionParticipantRepository(FormParticipant participant) : IFormParticipantRepository
{
    public Task<FormParticipant?> GetAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        Task.FromResult(tenantId == participant.TenantId && participantId == participant.Id ? participant : null);

    public Task<FormParticipant?> GetUnlinkedByEmailAsync(
        Guid tenantId,
        string email,
        CancellationToken ct = default
    ) => Task.FromResult<FormParticipant?>(null);

    public void Add(FormParticipant item) { }

    public void Untrack(FormParticipant item) { }
}

internal sealed class GrantedResponsibleUsers(params string[] granted) : IFormResponsibleUserValidator
{
    public Task<bool> IsActiveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> HasPermissionsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<string> permissions,
        CancellationToken ct = default
    ) => Task.FromResult(permissions.All(granted.Contains));
}

internal sealed class RecordingReassignmentClient(Func<FormFileReassignmentRequest, string>? outcome = null)
    : IFormFileReassignmentClient
{
    internal List<FormFileReassignmentRequest> Calls { get; } = [];
    internal bool Unavailable { get; set; }

    public Task<Result<IReadOnlyList<FormFileReassignmentResult>>> ReassignAsync(
        FormFileReassignmentRequest request,
        CancellationToken ct = default
    )
    {
        Calls.Add(request);
        if (Unavailable)
            return Task.FromResult(
                Result.Failure<IReadOnlyList<FormFileReassignmentResult>>(
                    new("FormRequest.FileReassignmentUnavailable", "x")
                )
            );
        var status = outcome?.Invoke(request) ?? FormFileReassignmentOutcomes.Reassigned;
        IReadOnlyList<FormFileReassignmentResult> results = request
            .FileIds.Select(id => new FormFileReassignmentResult(id, status))
            .ToArray();
        return Task.FromResult(Result.Success(results));
    }
}

/// <summary>
/// Bus con la promesa del outbox: lo publicado queda pendiente y solo sale si el guardado confirma.
/// </summary>
public class OutboxMessageBus : DispatchProxy
{
    internal List<object> Pending { get; } = [];
    internal List<object> Flushed { get; } = [];

    internal static (IMessageBus Bus, OutboxMessageBus Recorder) Create()
    {
        var bus = Create<IMessageBus, OutboxMessageBus>();
        return (bus, (OutboxMessageBus)(object)bus);
    }

    internal void Commit()
    {
        Flushed.AddRange(Pending);
        Pending.Clear();
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "PublishAsync")
            throw new NotSupportedException($"El doble de bus no implementa {targetMethod?.Name}.");
        Pending.Add(args![0]!);
        return ValueTask.CompletedTask;
    }
}

internal sealed class OutboxUnitOfWork(OutboxMessageBus bus, Exception? failure = null)
    : BuildingBlocks.Persistence.IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (failure is not null)
            throw failure;
        bus.Commit();
        return Task.FromResult(1);
    }
}
