using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Cambiar el idioma guarda y publica la foto del perfil (nombre, foto, idioma y zona).</summary>
public sealed class SetMyLanguageHandlerTests
{
    [Fact]
    public async Task Saves_and_publishes_profile_snapshot()
    {
        var user = AvatarDoubles.Employee();
        user.SetTimeZone("America/New_York");
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        var result = await HandleAsync(user, "ES", bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal("es", user.Language);
        Assert.Equal([0], unitOfWork.PublishedAtSave);
        var profile = Assert.IsType<UserProfileUpdatedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(user.Id, profile.UserId);
        Assert.Equal("es", profile.Language);
        Assert.Equal("America/New_York", profile.TimeZoneId);
        Assert.Equal("Elio", profile.Name);
    }

    [Fact]
    public async Task Unknown_language_is_rejected_without_saving()
    {
        var user = AvatarDoubles.Employee();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        var result = await HandleAsync(user, "pt", bus, unitOfWork);

        Assert.True(result.IsFailure);
        Assert.Equal("User.Language", result.Error.Code);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    private static Task<BuildingBlocks.Results.Result> HandleAsync(
        User user,
        string? language,
        FakeMessageBus bus,
        OrderedUnitOfWork unitOfWork
    ) =>
        SetMyLanguageHandler.Handle(
            new SetMyLanguageCommand(user.Id, language),
            AvatarDoubles.Users(user),
            new RecordingAvatarAudit(),
            new AvatarRequestContext(),
            new AvatarCorrelation(),
            unitOfWork,
            bus,
            CancellationToken.None
        );
}
