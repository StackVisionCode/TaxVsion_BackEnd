using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>El evento de perfil es una foto: sale si cambia el nombre o la zona, y guardar el perfil no toca el idioma.</summary>
public sealed class UpdateMyProfileHandlerTests
{
    [Fact]
    public async Task Changing_only_the_time_zone_publishes()
    {
        var user = AvatarDoubles.Employee();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        var result = await HandleAsync(user, "Elio", "Paz", "America/Chicago", bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal([0], unitOfWork.PublishedAtSave);
        var profile = Assert.IsType<UserProfileUpdatedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("America/Chicago", profile.TimeZoneId);
    }

    [Fact]
    public async Task Saving_without_changes_does_not_publish()
    {
        var user = AvatarDoubles.Employee();
        user.SetTimeZone("America/Chicago");
        var bus = new FakeMessageBus();

        var result = await HandleAsync(user, "Elio", "Paz", "America/Chicago", bus, new OrderedUnitOfWork(bus));

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Saving_the_profile_does_not_touch_the_language()
    {
        var user = AvatarDoubles.Employee();
        user.SetLanguage("es");
        var bus = new FakeMessageBus();

        var result = await HandleAsync(user, "Eliot", "Paz", null, bus, new OrderedUnitOfWork(bus));

        Assert.True(result.IsSuccess);
        Assert.Equal("es", user.Language);
        var profile = Assert.IsType<UserProfileUpdatedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("Eliot", profile.Name);
        Assert.Equal("es", profile.Language);
    }

    private static Task<BuildingBlocks.Results.Result> HandleAsync(
        User user,
        string name,
        string lastName,
        string? timeZoneId,
        FakeMessageBus bus,
        OrderedUnitOfWork unitOfWork
    ) =>
        UpdateMyProfileHandler.Handle(
            new UpdateMyProfileCommand(user.Id, name, lastName, timeZoneId),
            AvatarDoubles.Users(user),
            new RecordingAvatarAudit(),
            new AvatarRequestContext(),
            new AvatarCorrelation(),
            unitOfWork,
            bus,
            CancellationToken.None
        );
}
