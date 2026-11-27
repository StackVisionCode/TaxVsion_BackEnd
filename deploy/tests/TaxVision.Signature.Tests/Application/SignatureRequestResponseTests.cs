using System.Text.Json;
using TaxVision.Signature.Application.Requests;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Application;

/// <summary>
/// Al reabrir un borrador, el editor muestra los datos 8879 del preparador ya guardados: nombre, título y el
/// PTIN/EFIN enmascarado. El identificador completo nunca sale en la respuesta.
/// </summary>
public sealed class SignatureRequestResponseTests
{
    [Fact]
    public void The_saved_preparer_comes_back_masked()
    {
        var draft = NewDraft();
        draft.SetPreparer(PreparerInfo.Create("P12345678", "Carlos Castillo", "Tax Preparer").Value);

        var response = SignatureRequestResponse.From(draft);

        Assert.Equal("Carlos Castillo", response.PreparerDisplayName);
        Assert.Equal("Tax Preparer", response.PreparerTitleLabel);
        Assert.Equal("P••••5678", response.PreparerMaskedIdentifier);
        Assert.DoesNotContain("P12345678", JsonSerializer.Serialize(response));
    }

    [Fact]
    public void Without_preparer_there_is_nothing_to_show()
    {
        var response = SignatureRequestResponse.From(NewDraft());

        Assert.Null(response.PreparerTitleLabel);
        Assert.Null(response.PreparerMaskedIdentifier);
    }

    private static SignatureRequest NewDraft() =>
        SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                tenantId: Guid.NewGuid(),
                createdByUserId: Guid.NewGuid(),
                title: "Preparer details",
                description: null,
                category: "ConsentToDisclose",
                originalFileId: Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;
}
