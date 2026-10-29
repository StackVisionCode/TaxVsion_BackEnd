using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Users.Avatars;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Resultado del antivirus para fotos de perfil: confirma solo la pendiente, borra la anterior, descarta
/// en cada tipo de fallo, ignora lo que no es suyo y no repite efectos con un evento duplicado.
/// </summary>
public sealed class UserAvatarScanResultConsumerTests
{
    [Fact]
    public async Task FileAvailable_de_la_pendiente_confirma_borra_la_anterior_y_avisa_el_perfil()
    {
        var user = AvatarDoubles.Employee();
        var previous = AvatarDoubles.GiveConfirmedAvatar(user);
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, DateTime.UtcNow);
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        await AvailableAsync(user, Available(user, pending), unitOfWork, bus);

        Assert.Equal(pending, user.AvatarFileId);
        Assert.Null(user.PendingAvatarFileId);
        Assert.Equal([0], unitOfWork.PublishedAtSave);
        var deletion = Assert.IsType<FilesDeletionRequestedIntegrationEvent>(bus.Published[0]);
        Assert.Equal(previous, Assert.Single(deletion.Files).FileId);
        Assert.Equal("avatar_replaced", deletion.Reason);
        var profile = Assert.IsType<UserProfileUpdatedIntegrationEvent>(bus.Published[1]);
        Assert.Equal(pending, profile.AvatarFileId);
        Assert.Equal(user.Id, profile.UserId);
    }

    [Fact]
    public async Task FileAvailable_primera_foto_no_pide_borrar_nada()
    {
        var user = AvatarDoubles.Employee();
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, DateTime.UtcNow);
        var bus = new FakeMessageBus();

        await AvailableAsync(user, Available(user, pending), new OrderedUnitOfWork(bus), bus);

        Assert.IsType<UserProfileUpdatedIntegrationEvent>(Assert.Single(bus.Published));
    }

    [Fact]
    public async Task FileAvailable_repetido_de_la_confirmada_es_no_op()
    {
        var user = AvatarDoubles.Employee();
        var confirmed = AvatarDoubles.GiveConfirmedAvatar(user);
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        await AvailableAsync(user, Available(user, confirmed), unitOfWork, bus);

        Assert.Equal(confirmed, user.AvatarFileId);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task FileAvailable_de_una_foto_vieja_no_toca_la_pendiente_y_pide_borrarla()
    {
        var user = AvatarDoubles.Employee();
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, DateTime.UtcNow);
        var stale = Guid.NewGuid();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        await AvailableAsync(user, Available(user, stale), unitOfWork, bus);

        Assert.Equal(pending, user.PendingAvatarFileId);
        Assert.Null(user.AvatarFileId);
        Assert.Equal(0, unitOfWork.Saves);
        var deletion = Assert.IsType<FilesDeletionRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(stale, Assert.Single(deletion.Files).FileId);
        Assert.Equal("avatar_stale", deletion.Reason);
    }

    [Fact]
    public async Task FileAvailable_de_una_pendiente_vencida_la_descarta_y_pide_borrarla()
    {
        var user = AvatarDoubles.Employee();
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, DateTime.UtcNow.AddMinutes(-11));
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        await AvailableAsync(user, Available(user, pending), unitOfWork, bus);

        Assert.Null(user.AvatarFileId);
        Assert.Null(user.PendingAvatarFileId);
        Assert.Equal([0], unitOfWork.PublishedAtSave);
        var deletion = Assert.IsType<FilesDeletionRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(pending, Assert.Single(deletion.Files).FileId);
    }

    [Theory]
    [InlineData("Customer", "Avatars")]
    [InlineData("User", "Documents")]
    public async Task FileAvailable_de_otro_dueno_o_carpeta_se_ignora_sin_consultar(string ownerType, string folderType)
    {
        var bus = new FakeMessageBus();
        var throwingUsers = StubProxy<TaxVision.Auth.Application.Abstractions.IUserRepository>.Create(
            (_, _) => (false, null)
        );

        await UserAvatarScanResultConsumer.Handle(
            Available(AvatarDoubles.Employee(), Guid.NewGuid()) with
            {
                OwnerType = ownerType,
                FolderType = folderType,
            },
            throwingUsers,
            new OrderedUnitOfWork(bus),
            bus,
            new AvatarCorrelation(),
            NullLogger<User>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task FileAvailable_con_tenant_distinto_al_del_usuario_se_ignora()
    {
        var user = AvatarDoubles.Employee();
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, DateTime.UtcNow);
        var bus = new FakeMessageBus();

        await AvailableAsync(
            user,
            Available(user, pending) with
            {
                TenantId = Guid.NewGuid(),
            },
            new OrderedUnitOfWork(bus),
            bus
        );

        Assert.Equal(pending, user.PendingAvatarFileId);
        Assert.Empty(bus.Published);
    }

    public static TheoryData<string> Failures => new() { "infected", "blocked", "scan_failed" };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Cada_fallo_descarta_la_pendiente_y_conserva_la_confirmada(string failure)
    {
        var user = AvatarDoubles.Employee();
        var confirmed = AvatarDoubles.GiveConfirmedAvatar(user);
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, DateTime.UtcNow);
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        await FailAsync(failure, user, pending, unitOfWork);

        Assert.Null(user.PendingAvatarFileId);
        Assert.Equal(confirmed, user.AvatarFileId);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Fallo_de_una_foto_que_no_es_la_pendiente_no_cambia_nada(string failure)
    {
        var user = AvatarDoubles.Employee();
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, DateTime.UtcNow);
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        await FailAsync(failure, user, Guid.NewGuid(), unitOfWork);

        Assert.Equal(pending, user.PendingAvatarFileId);
        Assert.Equal(0, unitOfWork.Saves);
    }

    private static FileAvailableIntegrationEvent Available(User user, Guid fileId) =>
        new()
        {
            TenantId = user.TenantId,
            FileId = fileId,
            ObjectKey = $"tenants/{user.TenantId:N}/user/{user.Id:N}/avatars/{fileId:N}.webp",
            ContentType = "image/webp",
            SizeBytes = 1024,
            ChecksumSha256 = "abc",
            CreatedBy = user.Id,
            OwnerType = "User",
            OwnerId = user.Id,
            FolderType = "Avatars",
        };

    private static Task AvailableAsync(
        User user,
        FileAvailableIntegrationEvent msg,
        OrderedUnitOfWork unitOfWork,
        FakeMessageBus bus
    ) =>
        UserAvatarScanResultConsumer.Handle(
            msg,
            AvatarDoubles.Users(user),
            unitOfWork,
            bus,
            new AvatarCorrelation(),
            NullLogger<User>.Instance,
            CancellationToken.None
        );

    private static Task FailAsync(string failure, User user, Guid fileId, OrderedUnitOfWork unitOfWork)
    {
        var users = AvatarDoubles.Users(user);
        var correlation = new AvatarCorrelation();
        var logger = NullLogger<User>.Instance;
        return failure switch
        {
            "infected" => UserAvatarScanResultConsumer.Handle(
                new FileInfectedDetectedIntegrationEvent
                {
                    TenantId = user.TenantId,
                    FileId = fileId,
                    ObjectKey = "k",
                    ScanReport = "EICAR",
                    OwnerType = "User",
                    OwnerId = user.Id,
                    FolderType = "Avatars",
                },
                users,
                unitOfWork,
                correlation,
                logger,
                CancellationToken.None
            ),
            "blocked" => UserAvatarScanResultConsumer.Handle(
                new FileBlockedByPolicyIntegrationEvent
                {
                    TenantId = user.TenantId,
                    FileId = fileId,
                    ObjectKey = "k",
                    PolicyReason = "nsfw",
                    CreatedBy = user.Id,
                    OwnerType = "User",
                    OwnerId = user.Id,
                    FolderType = "Avatars",
                },
                users,
                unitOfWork,
                correlation,
                logger,
                CancellationToken.None
            ),
            _ => UserAvatarScanResultConsumer.Handle(
                new FileScanFailedIntegrationEvent
                {
                    TenantId = user.TenantId,
                    FileId = fileId,
                    ObjectKey = "k",
                    Reason = "content_type_mismatch",
                    OwnerType = "User",
                    OwnerId = user.Id,
                    FolderType = "Avatars",
                },
                users,
                unitOfWork,
                correlation,
                logger,
                CancellationToken.None
            ),
        };
    }
}
