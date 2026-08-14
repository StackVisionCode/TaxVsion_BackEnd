using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

/// <summary>La invariante que importa no es de formato: el texto no puede faltar.</summary>
public sealed class ClientRequestNoteTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void An_empty_request_is_rejected(string? value)
    {
        var result = ClientRequestNote.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.WaitingOnClient.ExpectedItemsRequired", result.Error.Code);
    }

    [Fact]
    public void A_request_longer_than_the_cap_is_rejected()
    {
        var result = ClientRequestNote.Create(new string('x', ClientRequestNote.MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("Task.WaitingOnClient.ExpectedItemsTooLong", result.Error.Code);
    }

    [Fact]
    public void The_cap_itself_is_accepted_and_the_text_is_trimmed()
    {
        Assert.True(ClientRequestNote.Create(new string('x', ClientRequestNote.MaxLength)).IsSuccess);

        var trimmed = ClientRequestNote.Create("  falta W-2 y 1099-INT  ");
        Assert.Equal("falta W-2 y 1099-INT", trimmed.Value.Value);
    }
}
