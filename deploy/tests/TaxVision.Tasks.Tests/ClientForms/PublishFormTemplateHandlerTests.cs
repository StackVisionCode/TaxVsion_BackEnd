using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class PublishFormTemplateHandlerTests
{
    [Fact]
    public async Task Publish_creates_an_immutable_version_and_audit()
    {
        var template = FormTemplateHandlerTestDoubles.Template();
        var versions = new MemoryFormTemplateVersionRepository();
        var audit = new RecordingFormAuditLog();
        var unit = new RecordingUnitOfWork();

        var result = await PublishFormTemplateHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, []),
            FormTemplateHandlerTestDoubles.Engine(),
            new MemoryFormTemplateRepository(template),
            versions,
            new MemoryConsentNoticeRepository(),
            audit,
            unit,
            default
        );

        Assert.True(result.Value.Published);
        Assert.Single(versions.Items);
        Assert.Equal([FormAuditAction.TemplatePublished], audit.Actions);
        Assert.Equal(1, unit.Saves);
    }

    [Fact]
    public async Task Publish_rejects_a_blocking_before_collection_consent_that_can_be_hidden()
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var canonical = engine.Canonicalize(engine.ParseDefinition(HiddenGateDefinition).Value);
        var template = FormTemplate
            .Create(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateName.Create("Hidden gate").Value,
                null,
                ClientFormsTestDocuments.Definition(canonical.Json),
                ClientFormsTestDocuments.Hash(canonical.Sha256),
                FormPurpose.ClientIntake,
                FormTemplateHandlerTestDoubles.UserId,
                DateTime.UtcNow
            )
            .Value;
        var notice = ConsentNotice
            .Create(
                template.TenantId,
                "privacy_notice",
                "Privacy",
                ConsentNoticePurpose.General,
                FormTemplateHandlerTestDoubles.UserId,
                DateTime.UtcNow
            )
            .Value;
        notice.Publish(
            "en",
            "{\"type\":\"doc\"}",
            "Privacy",
            new string('a', 64),
            FormTemplateHandlerTestDoubles.UserId,
            DateTime.UtcNow
        );
        var versions = new MemoryFormTemplateVersionRepository();

        var result = await PublishFormTemplateHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, []),
            engine,
            new MemoryFormTemplateRepository(template),
            versions,
            new MemoryConsentNoticeRepository(notice),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );

        Assert.False(result.Value.Published);
        Assert.Contains(result.Value.Issues, issue => issue.Code == "consent.before_collection_must_be_visible");
        Assert.Empty(versions.Items);
    }

    private const string HiddenGateDefinition = """
        {"schemaVersion":1,"title":"Hidden gate","purpose":"clientIntake","presentation":"wizard","settings":{"showProgress":true},"pages":[{"id":"pg_abcdefgh","title":"Page","sections":[{"id":"sc_abcdefgh","title":"Section","components":[{"id":"fd_trigger01","type":"text","label":"Referral code","props":{}},{"id":"fd_consent01","type":"consent","visibleWhen":{"field":"fd_trigger01","op":"isAnswered"},"props":{"noticeKey":"privacy_notice","noticeVersionNumber":1,"mode":"acceptance","timing":"beforeCollection","acceptanceText":"I agree.","blocking":true}}]}]}],"mappings":[],"actions":[]}
        """;
}
