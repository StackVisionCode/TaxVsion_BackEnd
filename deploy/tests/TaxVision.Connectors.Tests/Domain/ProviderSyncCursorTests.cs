using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Tests.Domain;

public class ProviderSyncCursorTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = ProviderSyncCursor.Create(AccountId, "history-1", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.Value.AccountId);
        Assert.Equal("history-1", result.Value.CursorValue);
        Assert.Equal(Now, result.Value.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithNullCursorValue_Succeeds()
    {
        var result = ProviderSyncCursor.Create(AccountId, cursorValue: null, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CursorValue);
    }

    [Fact]
    public void Create_WithEmptyAccountId_Fails()
    {
        var result = ProviderSyncCursor.Create(Guid.Empty, "history-1", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderSyncCursor.AccountId", result.Error.Code);
    }

    [Fact]
    public void UpdateCursor_ReplacesValueAndTimestamp()
    {
        var cursor = ProviderSyncCursor.Create(AccountId, "history-1", Now).Value;
        var updatedAt = Now.AddMinutes(5);

        cursor.UpdateCursor("history-2", updatedAt);

        Assert.Equal("history-2", cursor.CursorValue);
        Assert.Equal(updatedAt, cursor.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateCursor_WithNull_ClearsCursorValue()
    {
        var cursor = ProviderSyncCursor.Create(AccountId, "history-1", Now).Value;

        cursor.UpdateCursor(null, Now.AddMinutes(5));

        Assert.Null(cursor.CursorValue);
    }
}
