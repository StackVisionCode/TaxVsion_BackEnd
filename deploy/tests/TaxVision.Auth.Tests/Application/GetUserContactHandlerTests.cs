using TaxVision.Auth.Application.Users.Internal.Queries;

namespace TaxVision.Auth.Tests.Application;

/// <summary>La lectura interna de contacto lleva el idioma y la zona propios del usuario.</summary>
public sealed class GetUserContactHandlerTests
{
    [Fact]
    public async Task Returns_language_and_time_zone()
    {
        var user = AvatarDoubles.Employee();
        user.SetLanguage("es");
        user.SetTimeZone("America/New_York");

        var result = await GetUserContactHandler.Handle(
            new GetUserContactQuery(user.TenantId, user.Id),
            AvatarDoubles.Users(user),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("es", result.Value.Language);
        Assert.Equal("America/New_York", result.Value.TimeZoneId);
    }
}
