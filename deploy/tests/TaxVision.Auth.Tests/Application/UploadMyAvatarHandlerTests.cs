using BuildingBlocks.Imaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Users.Avatars;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// PUT /auth/me/avatar: normaliza, sube al bucket temporal, deja la foto pendiente y recién después de
/// guardar pide el catálogo a CloudStorage (si publicara antes, el resultado del antivirus podría
/// llegar sin fila y un reintento en línea filtraría el pedido).
/// </summary>
public sealed class UploadMyAvatarHandlerTests
{
    [Fact]
    public async Task Guarda_la_foto_pendiente_y_publica_el_pedido_de_catalogo_despues_de_guardar()
    {
        var user = AvatarDoubles.Employee();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);
        var storage = new FakeAvatarStorage();
        var audit = new RecordingAvatarAudit();

        var result = await HandleAsync(user, new FakeAvatarNormalizer(), storage, unitOfWork, bus, audit);

        Assert.True(result.IsSuccess);
        Assert.Equal("processing", result.Value.Status);
        var stored = Assert.Single(storage.Stored);
        Assert.Equal(FakeAvatarNormalizer.Output, stored.Content);
        Assert.Equal("image/webp", stored.ContentType);
        Assert.Equal(user.TenantId, stored.TenantId);
        Assert.Equal(stored.FileId, result.Value.FileId);
        Assert.Equal(stored.FileId, user.PendingAvatarFileId);

        // Guardar antes de publicar: al guardar no había salido nada.
        Assert.Equal([0], unitOfWork.PublishedAtSave);
        var request = Assert.IsType<SaveFileRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(stored.FileId, request.FileId);
        Assert.Equal("User", request.OwnerType);
        Assert.Equal(user.Id, request.OwnerId);
        Assert.Equal("Avatars", request.FolderType);
        Assert.Equal("image/webp", request.ContentType);
        Assert.Equal(FakeAvatarNormalizer.Output.LongLength, request.SizeBytes);
        Assert.Equal(user.TenantId, request.TenantId);
        Assert.Null(request.TaxYear);
        Assert.Equal(AuthAuditAction.UserAvatarUploaded, Assert.Single(audit.Logs).Action);
    }

    [Fact]
    public async Task Imagen_invalida_no_sube_ni_guarda_nada()
    {
        var user = AvatarDoubles.Employee();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);
        var storage = new FakeAvatarStorage();

        var result = await HandleAsync(
            user,
            new FakeAvatarNormalizer(AvatarImageErrors.UnsupportedFormat),
            storage,
            unitOfWork,
            bus
        );

        Assert.Equal("Avatar.UnsupportedFormat", result.Error.Code);
        Assert.Empty(storage.Stored);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
        Assert.Null(user.PendingAvatarFileId);
    }

    [Fact]
    public async Task Falla_de_MinIO_no_deja_pendiente_ni_publica()
    {
        var user = AvatarDoubles.Employee();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        var result = await HandleAsync(
            user,
            new FakeAvatarNormalizer(),
            new FakeAvatarStorage(fail: true),
            unitOfWork,
            bus
        );

        Assert.Equal("User.AvatarStorageUnavailable", result.Error.Code);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
        Assert.Null(user.PendingAvatarFileId);
    }

    [Fact]
    public async Task Cliente_del_portal_es_rechazado_antes_de_normalizar()
    {
        var portal = AvatarDoubles.PortalClient();
        var normalizer = new FakeAvatarNormalizer();
        var bus = new FakeMessageBus();

        var result = await HandleAsync(portal, normalizer, new FakeAvatarStorage(), new OrderedUnitOfWork(bus), bus);

        Assert.Equal("User.AvatarNotAllowed", result.Error.Code);
        Assert.Equal(0, normalizer.Calls);
    }

    [Fact]
    public async Task Usuario_de_otra_oficina_o_inactivo_es_not_found()
    {
        var user = AvatarDoubles.Employee();
        var bus = new FakeMessageBus();

        var otherTenant = await UploadMyAvatarHandler.Handle(
            new UploadMyAvatarCommand(Guid.NewGuid(), user.Id, [1]),
            AvatarDoubles.Users(user),
            new FakeAvatarNormalizer(),
            new FakeAvatarStorage(),
            new RecordingAvatarAudit(),
            new AvatarRequestContext(),
            new AvatarCorrelation(),
            new OrderedUnitOfWork(bus),
            bus,
            CancellationToken.None
        );
        user.Deactivate(DateTime.UtcNow);
        var inactive = await HandleAsync(
            user,
            new FakeAvatarNormalizer(),
            new FakeAvatarStorage(),
            new OrderedUnitOfWork(bus),
            bus
        );

        Assert.Equal("User.NotFound", otherTenant.Error.Code);
        Assert.Equal("User.NotFound", inactive.Error.Code);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Una_segunda_subida_reemplaza_la_pendiente_y_conserva_la_confirmada()
    {
        var user = AvatarDoubles.Employee();
        var confirmed = AvatarDoubles.GiveConfirmedAvatar(user);
        var bus = new FakeMessageBus();

        var first = await HandleAsync(
            user,
            new FakeAvatarNormalizer(),
            new FakeAvatarStorage(),
            new OrderedUnitOfWork(bus),
            bus
        );
        var second = await HandleAsync(
            user,
            new FakeAvatarNormalizer(),
            new FakeAvatarStorage(),
            new OrderedUnitOfWork(bus),
            bus
        );

        Assert.NotEqual(first.Value.FileId, second.Value.FileId);
        Assert.Equal(second.Value.FileId, user.PendingAvatarFileId);
        Assert.Equal(confirmed, user.AvatarFileId);
    }

    private static Task<Result<UploadMyAvatarResponse>> HandleAsync(
        User user,
        FakeAvatarNormalizer normalizer,
        FakeAvatarStorage storage,
        OrderedUnitOfWork unitOfWork,
        FakeMessageBus bus,
        RecordingAvatarAudit? audit = null
    ) =>
        UploadMyAvatarHandler.Handle(
            new UploadMyAvatarCommand(user.TenantId, user.Id, [0xFF, 0xD8, 0xFF]),
            AvatarDoubles.Users(user),
            normalizer,
            storage,
            audit ?? new RecordingAvatarAudit(),
            new AvatarRequestContext(),
            new AvatarCorrelation(),
            unitOfWork,
            bus,
            CancellationToken.None
        );
}
