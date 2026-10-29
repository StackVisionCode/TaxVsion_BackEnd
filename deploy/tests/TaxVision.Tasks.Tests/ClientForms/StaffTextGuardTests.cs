using TaxVision.Tasks.ClientForms.Application.Common;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class StaffTextGuardTests
{
    [Theory]
    [InlineData("Customer 123-45-6789")]
    [InlineData("Payment 4111 1111 1111 1111")]
    public void Rejects_sensitive_identifiers_in_staff_text(string value)
    {
        var result = StaffTextGuard.Validate(value);

        Assert.Equal(StaffTextGuard.SensitiveData, result.Error);
    }

    [Theory]
    [InlineData("2026 individual intake")]
    [InlineData("Form 1040 organizer")]
    [InlineData(null)]
    public void Accepts_normal_staff_text(string? value)
    {
        Assert.True(StaffTextGuard.Validate(value).IsSuccess);
    }
}
