using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// Foto de perfil del staff en el aggregate User: la nueva queda pendiente hasta el antivirus, solo la
/// pendiente se confirma o descarta, y un evento viejo o repetido no cambia nada.
/// </summary>
public sealed class UserAvatarTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static User Employee() =>
        User.Register(Guid.NewGuid(), "Elio", "Paz", "elio@example.com", "hash", UserActorType.TenantEmployee).Value;

    [Fact]
    public void BeginAvatarChange_deja_la_foto_pendiente_sin_tocar_la_confirmada()
    {
        var user = Employee();
        var current = Guid.NewGuid();
        user.BeginAvatarChange(current, Now);
        user.ConfirmAvatar(current);

        var next = Guid.NewGuid();
        var result = user.BeginAvatarChange(next, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(current, user.AvatarFileId);
        Assert.Equal(next, user.PendingAvatarFileId);
        Assert.Equal(Now, user.PendingAvatarRequestedAtUtc);
    }

    [Fact]
    public void BeginAvatarChange_rechaza_al_cliente_del_portal_y_el_id_vacio()
    {
        var portal = User.Register(
            Guid.NewGuid(),
            "Ada",
            "Lovelace",
            "ada@cliente.test",
            "hash",
            UserActorType.CustomerPortal,
            customerId: Guid.NewGuid()
        ).Value;

        Assert.Equal("User.AvatarNotAllowed", portal.BeginAvatarChange(Guid.NewGuid(), Now).Error.Code);
        Assert.Equal("User.AvatarFile", Employee().BeginAvatarChange(Guid.Empty, Now).Error.Code);
        Assert.Null(portal.PendingAvatarFileId);
    }

    [Fact]
    public void ConfirmAvatar_promueve_la_pendiente_y_devuelve_la_anterior()
    {
        var user = Employee();
        var first = Guid.NewGuid();
        user.BeginAvatarChange(first, Now);
        Assert.Null(user.ConfirmAvatar(first));

        var second = Guid.NewGuid();
        user.BeginAvatarChange(second, Now);
        var previous = user.ConfirmAvatar(second);

        Assert.Equal(first, previous);
        Assert.Equal(second, user.AvatarFileId);
        Assert.Null(user.PendingAvatarFileId);
        Assert.Null(user.PendingAvatarRequestedAtUtc);
    }

    [Fact]
    public void ConfirmAvatar_de_un_id_que_no_es_el_pendiente_no_cambia_nada()
    {
        var user = Employee();
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, Now);

        Assert.Null(user.ConfirmAvatar(Guid.NewGuid()));
        Assert.Null(user.AvatarFileId);
        Assert.Equal(pending, user.PendingAvatarFileId);
    }

    [Fact]
    public void ConfirmAvatar_repetido_es_un_no_op()
    {
        var user = Employee();
        var fileId = Guid.NewGuid();
        user.BeginAvatarChange(fileId, Now);
        user.ConfirmAvatar(fileId);

        Assert.Null(user.ConfirmAvatar(fileId));
        Assert.Equal(fileId, user.AvatarFileId);
    }

    [Fact]
    public void DiscardPendingAvatar_solo_descarta_la_pendiente_correcta()
    {
        var user = Employee();
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, Now);

        Assert.False(user.DiscardPendingAvatar(Guid.NewGuid()));
        Assert.Equal(pending, user.PendingAvatarFileId);

        Assert.True(user.DiscardPendingAvatar(pending));
        Assert.Null(user.PendingAvatarFileId);
        Assert.Null(user.PendingAvatarRequestedAtUtc);
    }

    [Fact]
    public void RemoveAvatar_quita_la_confirmada_y_la_pendiente_y_las_devuelve()
    {
        var user = Employee();
        var current = Guid.NewGuid();
        user.BeginAvatarChange(current, Now);
        user.ConfirmAvatar(current);
        var pending = Guid.NewGuid();
        user.BeginAvatarChange(pending, Now);

        var removed = user.RemoveAvatar();

        Assert.Equal([current, pending], removed);
        Assert.Null(user.AvatarFileId);
        Assert.Null(user.PendingAvatarFileId);
        Assert.Empty(user.RemoveAvatar());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void HasLivePendingAvatar_vence_a_los_diez_minutos(int minutesLater, bool expected)
    {
        var user = Employee();
        user.BeginAvatarChange(Guid.NewGuid(), Now);

        Assert.Equal(expected, user.HasLivePendingAvatar(Now.AddMinutes(minutesLater)));
    }

    [Fact]
    public void HasLivePendingAvatar_sin_pendiente_es_false() => Assert.False(Employee().HasLivePendingAvatar(Now));
}
