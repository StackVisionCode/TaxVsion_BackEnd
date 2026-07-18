using TaxVision.Connectors.Domain.Audit;

namespace TaxVision.Connectors.Tests.Domain;

public class ProviderConnectionAuditLogTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = ProviderConnectionAuditLog.Create(
            AccountId,
            ProviderConnectionAuditAction.BodyFetch,
            "Fetched body.",
            "Success",
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.Value.AccountId);
        Assert.Equal(ProviderConnectionAuditAction.BodyFetch, result.Value.Action);
        Assert.Equal("Fetched body.", result.Value.Detail);
        Assert.Equal("Success", result.Value.ResultCode);
        Assert.Equal(Now, result.Value.Timestamp);
    }

    [Fact]
    public void Create_WithEmptyAccountId_Fails()
    {
        var result = ProviderConnectionAuditLog.Create(
            Guid.Empty,
            ProviderConnectionAuditAction.BodyFetch,
            "Detail.",
            "Success",
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderConnectionAuditLog.AccountId", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankResultCode_Fails(string resultCode)
    {
        var result = ProviderConnectionAuditLog.Create(
            AccountId,
            ProviderConnectionAuditAction.BodyFetch,
            "Detail.",
            resultCode,
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderConnectionAuditLog.ResultCode", result.Error.Code);
    }

    [Fact]
    public void Create_WithResultCodeTooLong_Fails()
    {
        var resultCode = new string('r', 101);

        var result = ProviderConnectionAuditLog.Create(
            AccountId,
            ProviderConnectionAuditAction.BodyFetch,
            "Detail.",
            resultCode,
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderConnectionAuditLog.ResultCode", result.Error.Code);
    }

    [Fact]
    public void Create_WithDetailTooLong_TruncatesTo2000Chars()
    {
        var detail = new string('d', 2500);

        var result = ProviderConnectionAuditLog.Create(
            AccountId,
            ProviderConnectionAuditAction.BodyFetch,
            detail,
            "Success",
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2000, result.Value.Detail.Length);
    }
}
