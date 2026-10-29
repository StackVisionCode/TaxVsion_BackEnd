using System.Text;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Tests.Application;

// Caracterización del camino fiscal extraído: fija el comportamiento que compartían el handler y el consumer.
public sealed class FiscalTaxIdentifierServiceTests
{
    [Theory]
    [InlineData("123-45-6789", FiscalSubjectKind.Individual, "123456789")]
    [InlineData("12-3456789", FiscalSubjectKind.Business, "123456789")]
    public void Normalizes_valid_identifiers(string raw, FiscalSubjectKind kind, string expected)
    {
        var result = FiscalTaxIdentifierService.ValidateAndNormalize(raw, kind);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("000-11-2222", FiscalSubjectKind.Individual)]
    [InlineData("666-11-2222", FiscalSubjectKind.Individual)]
    [InlineData("123", FiscalSubjectKind.Individual)]
    [InlineData("1234", FiscalSubjectKind.Business)]
    public void Rejects_invalid_identifiers(string raw, FiscalSubjectKind kind)
    {
        var result = FiscalTaxIdentifierService.ValidateAndNormalize(raw, kind);

        Assert.True(result.IsFailure);
        Assert.Equal("FiscalProfile.TaxId", result.Error.Code);
    }

    [Fact]
    public void Encrypt_keeps_last4_and_uses_the_protector()
    {
        var encrypted = FiscalTaxIdentifierService.Encrypt("123456789", Guid.NewGuid(), new FakeProtector());

        Assert.Equal("6789", encrypted.Last4);
        Assert.Equal("bi:123456789", encrypted.BlindIndex);
        Assert.Equal("123456789", Encoding.UTF8.GetString(encrypted.Cipher));
    }

    [Theory]
    [InlineData("Ssn", FiscalSubjectKind.Individual)]
    [InlineData("Itin", FiscalSubjectKind.Individual)]
    [InlineData("Ein", FiscalSubjectKind.Business)]
    public void Maps_form_kind_to_subject_kind(string formKind, FiscalSubjectKind expected) =>
        Assert.Equal(expected, FiscalTaxIdentifierService.SubjectKindFor(formKind));

    private sealed class FakeProtector : ISensitiveDataProtector
    {
        public byte[] Protect(string plainText) => Encoding.UTF8.GetBytes(plainText);

        public string Unprotect(byte[] cipher) => Encoding.UTF8.GetString(cipher);

        public string ComputeBlindIndex(string plainText, Guid tenantId) => $"bi:{plainText}";
    }
}
