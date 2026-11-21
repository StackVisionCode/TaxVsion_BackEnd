using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>Los archivos de un form request se declaran con el request, su papel y el cliente (nulo si es externo).</summary>
public sealed class FormFileSourcesTests
{
    [Fact]
    public void An_attachment_declares_the_request_and_its_customer()
    {
        var request = CustomerRequest();
        var attachment = Register(request, Guid.NewGuid());

        var declared = FormFileSources.ForAttachment(request, attachment, "corr-1");

        Assert.Equal(
            (
                TenantId,
                attachment.FileId,
                "tasks",
                "form_request",
                request.Id,
                "attachment",
                (Guid?)CustomerId,
                "corr-1"
            ),
            (
                declared.TenantId,
                declared.FileId,
                declared.SourceService,
                declared.SourceResourceType,
                declared.SourceResourceId,
                declared.SourceRole,
                declared.RelatedCustomerId,
                declared.CorrelationId
            )
        );
    }

    [Fact]
    public void The_summary_declares_the_request_with_the_summary_role()
    {
        var request = CustomerRequest();
        var fileId = Guid.NewGuid();

        var declared = FormFileSources.ForSummary(request, fileId, "corr-1");

        Assert.Equal(
            (fileId, "form_request", request.Id, "summary_pdf", (Guid?)CustomerId),
            (
                declared.FileId,
                declared.SourceResourceType,
                declared.SourceResourceId,
                declared.SourceRole,
                declared.RelatedCustomerId
            )
        );
    }

    [Fact]
    public void An_unlinked_external_request_declares_no_customer()
    {
        var request = ExternalRequest();
        var attachment = Register(request, Guid.NewGuid());

        Assert.Null(FormFileSources.ForAttachment(request, attachment, "corr-1").RelatedCustomerId);
        Assert.Null(FormFileSources.ForSummary(request, Guid.NewGuid(), "corr-1").RelatedCustomerId);
    }

    internal static FormAttachment Register(FormRequest request, Guid fileId) =>
        request
            .Files.Register(
                new FormAnswerAddress(FileComponent, null),
                fileId,
                "return.pdf",
                "application/pdf",
                10,
                20,
                20,
                null,
                Now
            )
            .Value;

    internal static FormRequest ExternalRequest(Guid? tenantId = null, Guid? responsible = null) =>
        FormRequest
            .Create(
                tenantId ?? TenantId,
                FormParticipantRef.External(Guid.NewGuid()),
                Version(tenantId),
                null,
                null,
                2026,
                null,
                Now.AddDays(7),
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                responsible ?? UserId,
                UserId,
                Now
            )
            .Value;
}
