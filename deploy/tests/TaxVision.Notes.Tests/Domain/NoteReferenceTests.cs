using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Domain;

public class NoteReferenceTests
{
    [Fact]
    public void Create_WithNoneAndNoTargetId_Succeeds()
    {
        var result = NoteReference.Create(NoteTargetType.None, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(NoteTargetType.None, result.Value.TargetType);
        Assert.Null(result.Value.TargetId);
    }

    [Fact]
    public void Create_WithCustomerAndValidTargetId_Succeeds()
    {
        var targetId = Guid.NewGuid();

        var result = NoteReference.Create(NoteTargetType.Customer, targetId);

        Assert.True(result.IsSuccess);
        Assert.Equal(NoteTargetType.Customer, result.Value.TargetType);
        Assert.Equal(targetId, result.Value.TargetId);
    }

    [Fact]
    public void Create_WithCustomerAndNullTargetId_Fails()
    {
        var result = NoteReference.Create(NoteTargetType.Customer, null);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.ReferenceTargetRequired, result.Error);
    }

    [Fact]
    public void Create_WithCustomerAndEmptyTargetId_Fails()
    {
        var result = NoteReference.Create(NoteTargetType.Customer, Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.ReferenceTargetRequired, result.Error);
    }

    [Fact]
    public void Create_WithNoneIgnoresProvidedTargetId()
    {
        var result = NoteReference.Create(NoteTargetType.None, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.TargetId);
    }
}
