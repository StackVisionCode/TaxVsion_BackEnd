using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Users.Avatars;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>DELETE /auth/me/avatar: quita la foto, pide su borrado por evento y avisa el perfil nuevo.</summary>
public sealed class RemoveMyAvatarHandlerTests
{
    [Fact]
    public async Task Quita_la_foto_pide_borrar_confirmada_y_pendiente_y_avisa_el_perfil()
    {
        var user = AvatarDoubles.Employee();
        var confirmed = AvatarDoubles.GiveConfirmedAvatar(user);
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, DateTime.UtcNow);
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);
        var audit = new RecordingAvatarAudit();

        var result = await HandleAsync(user, unitOfWork, bus, audit);

        Assert.True(result.IsSuccess);
        Assert.Null(user.AvatarFileId);
        Assert.Null(user.PendingAvatarFileId);
        Assert.Equal([0], unitOfWork.PublishedAtSave);

        var deletion = Assert.IsType<FilesDeletionRequestedIntegrationEvent>(bus.Published[0]);
        Assert.Equal(user.TenantId, deletion.TenantId);
        Assert.Equal("avatar_removed", deletion.Reason);
        Assert.Equal([confirmed, pending], deletion.Files.Select(file => file.FileId));
        Assert.All(
            deletion.Files,
            file =>
            {
                Assert.Equal("User", file.ExpectedOwnerType);
                Assert.Equal(user.Id, file.ExpectedOwnerId);
                Assert.Equal("Avatars", file.ExpectedFolderType);
            }
        );

        var profile = Assert.IsType<UserProfileUpdatedIntegrationEvent>(bus.Published[1]);
        Assert.Null(profile.AvatarFileId);
        Assert.Equal(user.Name, profile.Name);
        Assert.Equal(AuthAuditAction.UserAvatarRemoved, Assert.Single(audit.Logs).Action);
    }

    [Fact]
    public async Task Sin_foto_es_idempotente_y_no_publica_nada()
    {
        var user = AvatarDoubles.Employee();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        var result = await HandleAsync(user, unitOfWork, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Solo_pendiente_pide_borrarla_sin_avisar_cambio_de_perfil()
    {
        var user = AvatarDoubles.Employee();
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, DateTime.UtcNow);
        var bus = new FakeMessageBus();

        await HandleAsync(user, new OrderedUnitOfWork(bus), bus);

        var deletion = Assert.IsType<FilesDeletionRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(pending, Assert.Single(deletion.Files).FileId);
    }

    [Fact]
    public async Task Cliente_del_portal_es_rechazado()
    {
        var bus = new FakeMessageBus();

        var result = await HandleAsync(AvatarDoubles.PortalClient(), new OrderedUnitOfWork(bus), bus);

        Assert.Equal("User.AvatarNotAllowed", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    private static Task<Result> HandleAsync(
        User user,
        OrderedUnitOfWork unitOfWork,
        FakeMessageBus bus,
        RecordingAvatarAudit? audit = null
    ) =>
        RemoveMyAvatarHandler.Handle(
            new RemoveMyAvatarCommand(user.TenantId, user.Id),
            AvatarDoubles.Users(user),
            audit ?? new RecordingAvatarAudit(),
            new AvatarRequestContext(),
            new AvatarCorrelation(),
            unitOfWork,
            bus,
            CancellationToken.None
        );
}
