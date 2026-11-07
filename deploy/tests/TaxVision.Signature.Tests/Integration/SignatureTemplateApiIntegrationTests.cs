using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Templates;
using TaxVision.Signature.Domain.Templates.ValueObjects;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>SIG-8 por HTTP, con el host real sobre base temporal: editar una plantilla publicada es 409.</summary>
public sealed class SignatureTemplateApiIntegrationTests(SchedulerHostFixture host)
    : IClassFixture<SchedulerHostFixture>
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact(Timeout = 120_000)]
    public async Task UpdateTemplateMetadata_PublishedTemplate_Returns409()
    {
        var templateId = await SeedPublishedAsync();
        var client = host.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokenFactory.Mint(host.Factory, _tenantId, Guid.NewGuid())
        );

        var response = await client.PutAsJsonAsync(
            $"/signature/templates/{templateId}/metadata",
            new
            {
                title = "New title",
                description = (string?)null,
                category = "Fiscal",
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Signature.Template.NotDraft", error.GetProperty("code").GetString());
    }

    private async Task<Guid> SeedPublishedAsync()
    {
        var limits = TestSignatureLimits.Default;
        var template = SignatureTemplate
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Published", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = template.AddDocument(limits, Guid.NewGuid(), "Return").Value;
        template.AddSlot(
            limits,
            TestSignatureLimits.MaxRolesPerTemplate,
            TemplateSlotRole.Create("Taxpayer").Value,
            "En"
        );
        template.PlaceField(
            document.Id,
            1,
            SignatureFieldKind.Signature,
            FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
            null,
            true
        );
        Assert.True(template.Publish().IsSuccess);

        await using var db = host.Database.NewContext(_tenantId);
        db.SignatureTemplates.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
    }
}
