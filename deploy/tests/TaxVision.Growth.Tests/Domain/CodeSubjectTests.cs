using Microsoft.Extensions.Options;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Growth.SharedKernel.Buyers;

namespace TaxVision.Growth.Tests.Domain;

/// <summary>
/// N11: para cupones y referidos una persona es un comprador aunque use alias de su correo. El email se normaliza solo
/// para comparar y se guarda únicamente como HMAC.
/// </summary>
public sealed class CodeSubjectTests
{
    private static readonly HmacBuyerIdentityHasher Hasher = new(
        Options.Create(new CodeTokenHashingOptions { Pepper = new string('p', 48) })
    );

    [Fact]
    public void Same_email_with_different_case_and_spaces_is_the_same_buyer() =>
        AssertSameBuyer("  Ana.Perez@Example.COM ", "ana.perez@example.com");

    [Fact]
    public void Plus_tag_is_removed_on_any_domain()
    {
        AssertSameBuyer("ana+promo@example.com", "ana@example.com");
        AssertSameBuyer("ana+2+x@outlook.com", "ana@outlook.com");
    }

    [Fact]
    public void Gmail_dots_and_googlemail_are_the_same_buyer()
    {
        AssertSameBuyer("a.n.a@gmail.com", "ana@gmail.com");
        AssertSameBuyer("A.na+promo@googlemail.com", "ana@gmail.com");
    }

    [Fact]
    public void Yahoo_dash_alias_is_the_same_buyer()
    {
        AssertSameBuyer("ana-shopping@yahoo.com", "ana@yahoo.com");
        AssertSameBuyer("ana-x@ymail.com", "ana@ymail.com");
    }

    [Fact]
    public void Icloud_me_and_mac_are_the_same_buyer()
    {
        AssertSameBuyer("ana@me.com", "ana@icloud.com");
        AssertSameBuyer("ana@mac.com", "ana@icloud.com");
    }

    [Fact]
    public void Outlook_and_hotmail_stay_different_buyers()
    {
        Assert.NotEqual(Buyer("ana@outlook.com").SubjectId, Buyer("ana@hotmail.com").SubjectId);
        // Fuera de Gmail los puntos sí cuentan.
        Assert.NotEqual(Buyer("a.na@example.com").SubjectId, Buyer("ana@example.com").SubjectId);
    }

    [Theory]
    [InlineData("ana@mailinator.com")]
    [InlineData("ana@sub.yopmail.com")]
    public void Disposable_domain_is_rejected_for_codes(string email)
    {
        var domains = new FileDisposableEmailDomains();

        Assert.True(domains.IsDisposable(NormalizedEmail.Create(email).Value));
        Assert.False(domains.IsDisposable(NormalizedEmail.Create("ana@gmail.com").Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ana")]
    [InlineData("ana@")]
    [InlineData("@example.com")]
    [InlineData("a b@example.com")]
    [InlineData("ana@example")]
    [InlineData("+@gmail.com")]
    public void Invalid_email_is_rejected(string email)
    {
        var result = NormalizedEmail.Create(email);

        Assert.True(result.IsFailure);
        Assert.Equal("Codes.Buyer.InvalidEmail", result.Error.Code);
    }

    [Fact]
    public void Buyer_id_does_not_contain_the_email()
    {
        var email = NormalizedEmail.Create("ana.perez@example.com").Value;
        var buyer = Hasher.Hash(email);

        Assert.DoesNotContain("ana", buyer.SubjectId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example", buyer.SubjectId, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith($"v{BuyerIdentity.CurrentVersion}:", buyer.SubjectId);
        // Ni el valor normalizado se escapa por ToString (logs).
        Assert.DoesNotContain("ana", email.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static BuyerIdentity Buyer(string email) => Hasher.Hash(NormalizedEmail.Create(email).Value);

    private static void AssertSameBuyer(string alias, string canonical)
    {
        Assert.Equal(canonical, NormalizedEmail.Create(alias).Value.Value);
        Assert.Equal(Buyer(canonical).SubjectId, Buyer(alias).SubjectId);
    }
}
