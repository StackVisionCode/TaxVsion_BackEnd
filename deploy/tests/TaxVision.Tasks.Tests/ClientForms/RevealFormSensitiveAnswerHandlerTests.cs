using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class RevealFormSensitiveAnswerHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid CustomerId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Submitted_answer_is_revealed_and_audited_with_request_origin_without_plaintext()
    {
        var request = Request();
        var answer = Capture(request);
        Submit(request, answer.Id);
        var audit = new RecordingFormAuditLog();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await Reveal(request, answer.Id, true, "123456789", audit, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal("123-45-6789", result.Value.Value);
        Assert.Equal("granted", Assert.Single(audit.StaffOutcomes).Outcome);
        Assert.DoesNotContain("123456789", audit.StaffOutcomes[0].Details);
        Assert.Equal(Origin, Assert.Single(audit.RequestContexts));
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Draft_only_answer_is_not_revealed_and_the_denial_is_audited()
    {
        var request = Request();
        var answer = Capture(request);
        var audit = new RecordingFormAuditLog();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await Reveal(request, answer.Id, true, "123456789", audit, unitOfWork);

        Assert.True(result.IsFailure);
        Assert.Equal("FormSensitiveAnswer.NotFound", result.Error.Code);
        Assert.Equal("denied.no_sensitive_answer", Assert.Single(audit.StaffOutcomes).Outcome);
        Assert.Equal(Origin, Assert.Single(audit.RequestContexts));
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Invisible_request_is_hidden_and_denial_is_persisted_with_request_origin()
    {
        var request = Request();
        var audit = new RecordingFormAuditLog();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await Reveal(request, Guid.NewGuid(), false, "unused", audit, unitOfWork);

        Assert.True(result.IsFailure);
        Assert.Equal("FormRequest.NotFound", result.Error.Code);
        Assert.Equal("denied.not_found", Assert.Single(audit.StaffOutcomes).Outcome);
        Assert.Equal(Origin, Assert.Single(audit.RequestContexts));
        Assert.Equal(1, unitOfWork.Saves);
    }

    private static readonly FormAuditRequestContext Origin = new("203.0.113.7", "Mozilla/5.0", "corr-123");

    private static Task<Result<RevealedFormSensitiveAnswerResponse>> Reveal(
        FormRequest request,
        Guid answerId,
        bool visible,
        string plaintext,
        RecordingFormAuditLog audit,
        RecordingUnitOfWork unitOfWork
    ) =>
        RevealFormSensitiveAnswerHandler.Handle(
            new RevealFormSensitiveAnswerCommand(
                TenantId,
                UserId,
                false,
                request.Id,
                answerId,
                Origin.IpAddress,
                Origin.UserAgent,
                Origin.CorrelationId
            ),
            new MemoryRequestRepository(request),
            new FixedVisibilityFilter(visible),
            new FixedSensitiveProtector(plaintext),
            new PlainAnswersProtector(),
            audit,
            unitOfWork,
            default
        );

    private static FormSensitiveAnswer Capture(FormRequest request) =>
        request
            .CaptureSensitiveAnswer(
                new FormAnswerAddress("fd_ssn", null),
                FormTaxIdentifierKind.Ssn,
                new ProtectedTaxIdentifier("protected", 1, "6789"),
                FormActor.PortalUser(UserId),
                Now
            )
            .Value;

    private static void Submit(FormRequest request, Guid answerId) =>
        Assert.True(
            request
                .Submit(
                    new ProtectedAnswers(
                        "{\"schemaVersion\":1,\"values\":{\"fd_ssn\":{\"sensitiveRef\":\""
                            + answerId
                            + "\",\"last4\":\"6789\",\"kind\":\"ssn\"}}}",
                        1
                    ),
                    [],
                    [answerId],
                    FormActor.PortalUser(UserId),
                    Now
                )
                .IsSuccess
        );

    private static FormRequest Request() =>
        FormRequest
            .Create(
                TenantId,
                FormParticipantRef.Customer(CustomerId),
                FormTemplateVersion.Create(
                    TenantId,
                    Guid.NewGuid(),
                    1,
                    "Tax organizer",
                    FormPurpose.Questionnaire,
                    FormPresentation.Wizard,
                    ClientFormsTestDocuments.Definition("{\"schemaVersion\":1,\"pages\":[]}"),
                    ClientFormsTestDocuments.Hash(new string('a', 64)),
                    0,
                    UserId,
                    Now
                ),
                null,
                null,
                2026,
                null,
                Now.AddDays(7),
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                null,
                UserId,
                Now
            )
            .Value;

    private sealed class MemoryRequestRepository(FormRequest request) : IFormRequestRepository
    {
        public void Add(FormRequest item) { }

        public Task<Result<FormRequest>> GetForCommandAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) => GetForReviewAsync(tenantId, requestId, ct);

        public Task<Result<FormRequest>> GetForReviewAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                tenantId == request.TenantId && requestId == request.Id
                    ? Result.Success(request)
                    : Result.Failure<FormRequest>(FormRequestErrors.NotFound)
            );

        public Task<bool> HasOpenRequestAsync(
            Guid tenantId,
            Guid customerId,
            Guid templateId,
            Guid? exceptRequestId = null,
            CancellationToken ct = default
        ) => Task.FromResult(false);

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

        public Task<IReadOnlyList<FormRequest>> ListByAttachmentFileIdAsync(
            Guid fileId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);
    }

    private sealed class FixedVisibilityFilter(bool visible) : IFormRequestVisibilityFilter
    {
        public Task<bool> CanViewCustomerAsync(
            Guid tenantId,
            Guid actorUserId,
            bool canViewAll,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(visible);

        public Task<bool> CanViewAsync(
            Guid tenantId,
            Guid actorUserId,
            bool canViewAll,
            FormRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(visible);
    }

    private sealed class PlainAnswersProtector : IAnswersProtector
    {
        public ProtectedAnswers Protect(string json) => new(json, 1);

        public string Unprotect(ProtectedAnswers value) => value.Cipher;
    }

    private sealed class FixedSensitiveProtector(string value) : ISensitiveValueProtector
    {
        public ProtectedSensitiveValue Protect(string normalized) => new("protected", 1);

        public string Unprotect(ProtectedSensitiveValue protectedValue) => value;
    }
}
