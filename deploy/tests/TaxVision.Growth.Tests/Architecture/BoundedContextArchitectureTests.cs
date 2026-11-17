using TaxVision.Codes.Domain.Definitions;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Growth.Tests.Architecture;

public sealed class BoundedContextArchitectureTests
{
    [Fact]
    public void Codes_domain_does_not_reference_referrals_or_deployment_layers()
    {
        AssertForbiddenReferences(
            typeof(CodeDefinition).Assembly,
            "TaxVision.Referrals",
            "TaxVision.Growth.Infrastructure",
            "TaxVision.Growth.Api"
        );
    }

    [Fact]
    public void Referrals_domain_does_not_reference_codes_or_deployment_layers()
    {
        AssertForbiddenReferences(
            typeof(ReferralProgram).Assembly,
            "TaxVision.Codes",
            "TaxVision.Growth.Infrastructure",
            "TaxVision.Growth.Api"
        );
    }

    [Fact]
    public void Application_layers_do_not_reference_the_other_bounded_context()
    {
        AssertForbiddenReferences(
            typeof(TaxVision.Codes.Application.Quotes.CreateQuote.CreateQuoteCommand).Assembly,
            "TaxVision.Referrals"
        );
        AssertForbiddenReferences(
            typeof(TaxVision.Referrals.Application.Qualifications.QualifyReferral.QualifyReferralCommand).Assembly,
            "TaxVision.Codes"
        );
    }

    [Fact]
    public void Shared_kernel_depends_on_no_bounded_context_or_technical_layer()
    {
        // Lo comparten Codes y Referrals a propósito: no puede depender de ninguno ni de EF/Wolverine.
        AssertForbiddenReferences(
            typeof(TaxVision.Growth.SharedKernel.Buyers.NormalizedEmail).Assembly,
            "TaxVision.Codes",
            "TaxVision.Referrals",
            "TaxVision.Growth.Infrastructure",
            "TaxVision.Growth.Api",
            "Microsoft.EntityFrameworkCore",
            "Wolverine"
        );
    }

    [Fact]
    public void A_buyer_identity_is_only_created_by_the_hasher()
    {
        var buyer = typeof(TaxVision.Growth.SharedKernel.Buyers.BuyerIdentity);

        Assert.Empty(buyer.GetConstructors());
        Assert.DoesNotContain(
            buyer.GetMethods(),
            method => method.IsStatic && method.ReturnType == buyer && method.IsPublic
        );
    }

    private static void AssertForbiddenReferences(
        System.Reflection.Assembly assembly,
        params string[] forbiddenPrefixes
    )
    {
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();

        foreach (var forbiddenPrefix in forbiddenPrefixes)
        {
            Assert.DoesNotContain(
                references,
                reference => reference.StartsWith(forbiddenPrefix, StringComparison.Ordinal)
            );
        }
    }
}
