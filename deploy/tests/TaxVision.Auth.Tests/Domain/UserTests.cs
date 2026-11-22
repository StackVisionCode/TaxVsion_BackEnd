using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Idioma de los avisos del usuario: solo "en", "es" o sin preferencia.</summary>
public sealed class UserTests
{
    [Theory]
    [InlineData("pt")]
    [InlineData("en-US")]
    [InlineData("Spanish")]
    [InlineData("  ")]
    public void SetLanguage_rejects_unknown_values(string language)
    {
        var user = Employee();
        user.SetLanguage("es");

        var result = user.SetLanguage(language);

        Assert.True(result.IsFailure);
        Assert.Equal("User.Language", result.Error.Code);
        Assert.Equal("es", user.Language);
    }

    [Theory]
    [InlineData("ES", "es")]
    [InlineData(" En ", "en")]
    [InlineData(null, null)]
    public void SetLanguage_normalizes_case(string? language, string? expected)
    {
        var user = Employee();

        var result = user.SetLanguage(language);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, user.Language);
    }

    private static User Employee() =>
        User.Register(Guid.NewGuid(), "Elio", "Paz", "elio@example.com", "hash", UserActorType.TenantEmployee).Value;
}
