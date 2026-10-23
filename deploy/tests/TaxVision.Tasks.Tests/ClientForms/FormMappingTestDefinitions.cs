using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Application.Templates.Commands;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.ClientForms;

internal static class FormMappingTestDefinitions
{
    internal static readonly PublisherCapabilities NoPermissions = new(
        new HashSet<string>(StringComparer.Ordinal),
        false
    );
    internal static readonly PublisherCapabilities Admin = new(
        new HashSet<string>(["customers.manage"], StringComparer.Ordinal),
        true
    );

    // First, Last, Email, Phone, DOB, Language y Address hacia CreateCustomer, con el Kind como constante.
    internal const string Onboarding = """
        {"schemaVersion":1,"title":"Client onboarding","purpose":"clientIntake","presentation":"wizard","pages":[{"id":"pg_abcdefgh","title":"About you","sections":[{"id":"sc_abcdefgh","title":"About you","components":[
        {"id":"fd_first0001","type":"text","label":"First name","required":true,"props":{"maxLength":80}},
        {"id":"fd_last00001","type":"text","label":"Last name","required":true,"props":{"maxLength":80}},
        {"id":"fd_email0001","type":"email","label":"Email","required":true,"props":{}},
        {"id":"fd_phone0001","type":"phone","label":"Phone","props":{}},
        {"id":"fd_dob000001","type":"date","label":"Date of birth","props":{"max":"today"}},
        {"id":"fd_lang00001","type":"radio","label":"Preferred language","required":true,"props":{"options":[{"value":"spanish","label":"Spanish"},{"value":"english","label":"English"}]}},
        {"id":"fd_addr00001","type":"address","label":"Home address","props":{"country":"US"}}]}]}],
        "mappings":[
        {"componentId":"fd_first0001","targetKey":"customer.person.first_name","transform":"trim"},
        {"componentId":"fd_last00001","targetKey":"customer.person.last_name","transform":"trim"},
        {"componentId":"fd_email0001","targetKey":"customer.primary_email","transform":"lowercase"},
        {"componentId":"fd_phone0001","targetKey":"customer.primary_phone"},
        {"componentId":"fd_dob000001","targetKey":"customer.date_of_birth"},
        {"componentId":"fd_lang00001","targetKey":"customer.language","optionMap":{"spanish":"Es","english":"En"}},
        {"componentId":"fd_addr00001","targetKey":"customer.address.home"}],
        "actions":[{"key":"create_client","type":"createCustomer","trigger":"onSubmit","config":{"kind":{"constant":"Individual"},"language":{"mapped":true}}}]}
        """;

    internal static FormTemplate Template(string definitionJson)
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var canonical = engine.Canonicalize(engine.ParseDefinition(definitionJson).Value);
        return FormTemplate
            .Create(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateName.Create("Client onboarding").Value,
                null,
                ClientFormsTestDocuments.Definition(canonical.Json),
                ClientFormsTestDocuments.Hash(canonical.Sha256),
                FormPurpose.ClientIntake,
                FormTemplateHandlerTestDoubles.UserId,
                DateTime.UtcNow
            )
            .Value;
    }

    internal static Task<Result<PublishFormTemplateOutcome>> PublishAsync(
        FormTemplate template,
        PublisherCapabilities capabilities,
        MemoryFormTemplateVersionRepository versions,
        IDataCollectionTargetCatalog? catalog = null
    ) =>
        PublishFormTemplateHandler.Handle(
            new(template.TenantId, FormTemplateHandlerTestDoubles.UserId, template.Id, [], capabilities),
            FormTemplateHandlerTestDoubles.Engine(),
            new MemoryFormTemplateRepository(template),
            versions,
            new MemoryConsentNoticeRepository(),
            catalog ?? FormTemplateHandlerTestDoubles.TargetCatalog(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            new RecordingClientFormsMetrics(),
            default
        );
}
