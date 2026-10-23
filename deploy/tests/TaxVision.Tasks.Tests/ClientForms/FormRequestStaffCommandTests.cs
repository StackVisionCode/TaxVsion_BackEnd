using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormRequestStaffCommandTests
{
    private const string TextWithSsn = "Please confirm 123-45-6789";

    [Theory]
    [InlineData(TextWithSsn, null)]
    [InlineData(null, "Card 4111 1111 1111 1111")]
    public async Task Assign_rejects_a_title_or_message_with_sensitive_data(string? title, string? message)
    {
        var result = await Assign(title, message, null);

        Assert.Equal("FormRequest.TextContainsSensitiveData", result.Error.Code);
    }

    [Fact]
    public async Task Assign_rejects_a_prefill_source_until_prefill_exists()
    {
        var result = await Assign(null, null, Guid.NewGuid());

        Assert.Equal(FormRequestErrors.PrefillNotAvailable, result.Error);
    }

    [Fact]
    public async Task Return_rejects_a_message_with_sensitive_data()
    {
        var (request, version) = Submitted();

        var result = await Return(request, version, TextWithSsn, []);

        Assert.Equal("FormRequest.TextContainsSensitiveData", result.Error.Code);
        Assert.Equal(FormRequestStatus.Submitted, request.Status);
    }

    [Fact]
    public async Task Return_rejects_a_field_comment_with_sensitive_data()
    {
        var (request, version) = Submitted();

        var result = await Return(
            request,
            version,
            "Please review",
            [new FormFieldCommentInput(new FormAnswerAddress(FileComponent, null), TextWithSsn)]
        );

        Assert.Equal("FormRequest.TextContainsSensitiveData", result.Error.Code);
    }

    [Theory]
    [InlineData("fd_unknown01", null)]
    [InlineData(FileComponent, "rw_abcdefgh")]
    public async Task Return_rejects_comments_outside_the_pinned_version(string componentId, string? rowId)
    {
        var (request, version) = Submitted();

        var result = await Return(
            request,
            version,
            "Please review",
            [new FormFieldCommentInput(new FormAnswerAddress(componentId, rowId), "Upload the signed copy.")]
        );

        Assert.Equal(FormRequestErrors.InvalidComponent, result.Error);
        Assert.Equal(FormRequestStatus.Submitted, request.Status);
    }

    [Fact]
    public async Task Return_accepts_a_comment_on_a_field_of_the_pinned_version()
    {
        var (request, version) = Submitted();
        var bus = new FakeMessageBus();

        var result = await Return(
            request,
            version,
            "Please review",
            [new FormFieldCommentInput(new FormAnswerAddress(FileComponent, null), "Upload the signed copy.")],
            bus
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FormRequestStatus.Returned, request.Status);
        Assert.IsType<FormRequestReturnedIntegrationEvent>(Assert.Single(bus.Published));
    }

    [Fact]
    public async Task Complete_rejects_an_internal_note_with_sensitive_data()
    {
        var (request, _) = Submitted();

        var result = await CompleteFormRequestHandler.Handle(
            new CompleteFormRequestCommand(TenantId, UserId, true, request.Id, TextWithSsn, request.RowVersion, Now),
            new SingleFormRequestRepository(request),
            new FixedRequestVisibility(true),
            null!,
            null!,
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new TestCorrelationContext(),
            default
        );

        Assert.Equal("FormRequest.TextContainsSensitiveData", result.Error.Code);
        Assert.Equal(FormRequestStatus.Submitted, request.Status);
    }

    [Fact]
    public async Task Cancel_rejects_a_note_with_sensitive_data()
    {
        var request = CustomerRequest();

        var result = await CancelFormRequestHandler.Handle(
            new CancelFormRequestCommand(TenantId, UserId, true, request.Id, TextWithSsn, request.RowVersion, Now),
            new SingleFormRequestRepository(request),
            new FixedRequestVisibility(true),
            null!,
            null!,
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new TestCorrelationContext(),
            default
        );

        Assert.Equal("FormRequest.TextContainsSensitiveData", result.Error.Code);
        Assert.Equal(FormRequestStatus.Pending, request.Status);
    }

    // La validación de entrada corre antes de tocar cualquier dependencia.
    private static Task<BuildingBlocks.Results.Result<TaxVision.Tasks.ClientForms.Application.Requests.FormRequestResponse>> Assign(
        string? title,
        string? message,
        Guid? prefillFrom
    ) =>
        AssignFormRequestHandler.Handle(
            new AssignFormRequestCommand(
                TenantId,
                UserId,
                true,
                CustomerId,
                Guid.NewGuid(),
                title,
                message,
                null,
                null,
                null,
                prefillFrom,
                Now
            ),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new TestCorrelationContext(),
            default
        );

    private static Task<BuildingBlocks.Results.Result<TaxVision.Tasks.ClientForms.Application.Requests.FormRequestResponse>> Return(
        FormRequest request,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion version,
        string message,
        IReadOnlyList<FormFieldCommentInput> comments,
        FakeMessageBus? bus = null
    ) =>
        ReturnFormRequestHandler.Handle(
            new ReturnFormRequestCommand(
                TenantId,
                UserId,
                true,
                request.Id,
                message,
                comments,
                request.RowVersion,
                Now
            ),
            new SingleFormRequestRepository(request),
            new FixedRequestVisibility(true),
            new MemoryFormTemplateVersionRepository(version),
            FormTemplateHandlerTestDoubles.Engine(),
            new PlainFormAnswersProtector(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            bus ?? new FakeMessageBus(),
            new TestCorrelationContext(),
            default
        );

    private static (
        FormRequest Request,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion Version
    ) Submitted()
    {
        var version = Version();
        var request = CustomerRequest(version);
        FormRequestFixtures.Submit(request);
        return (request, version);
    }
}
