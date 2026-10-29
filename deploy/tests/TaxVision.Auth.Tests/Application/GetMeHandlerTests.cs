using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Queries;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// /auth/me trae la foto confirmada y si hay otra pasando el antivirus. Una pendiente de más de diez
/// minutos se reporta como no pendiente: para el front ya falló y vuelve a las iniciales.
/// </summary>
public sealed class GetMeHandlerTests
{
    [Fact]
    public async Task Sin_foto_no_hay_avatar_ni_pendiente()
    {
        var me = await HandleAsync(AvatarDoubles.Employee());

        Assert.Null(me.AvatarFileId);
        Assert.False(me.AvatarPending);
    }

    [Fact]
    public async Task Foto_confirmada_y_otra_pendiente_reciente()
    {
        var user = AvatarDoubles.Employee();
        var avatar = AvatarDoubles.GiveConfirmedAvatar(user);
        user.BeginAvatarChange(Guid.NewGuid(), DateTime.UtcNow);

        var me = await HandleAsync(user);

        Assert.Equal(avatar, me.AvatarFileId);
        Assert.True(me.AvatarPending);
    }

    [Fact]
    public async Task Pendiente_vencida_se_reporta_como_no_pendiente()
    {
        var user = AvatarDoubles.Employee();
        user.BeginAvatarChange(Guid.NewGuid(), DateTime.UtcNow - User.AvatarPendingTimeout - TimeSpan.FromMinutes(1));

        var me = await HandleAsync(user);

        Assert.False(me.AvatarPending);
        Assert.Null(me.AvatarFileId);
    }

    private static async Task<MeResponse> HandleAsync(User user)
    {
        var tenants = StubProxy<ITenantRegistry>.Create(
            (method, _) =>
                method.Name == nameof(ITenantRegistry.GetByIdAsync)
                    ? (
                        true,
                        Task.FromResult<Tenant?>(
                            Tenant.Register(user.TenantId, "Oficina", "oficina", TenantKind.Customer, "UTC").Value
                        )
                    )
                    : (false, null)
        );
        var roles = StubProxy<IRoleRepository>.Create(
            (method, _) =>
                method.Name switch
                {
                    nameof(IRoleRepository.GetUserRolesAsync) => (true, Task.FromResult<IReadOnlyList<Role>>([])),
                    nameof(IRoleRepository.GetEffectivePermissionCodesAsync) => (
                        true,
                        Task.FromResult<IReadOnlyList<string>>([])
                    ),
                    _ => (false, null),
                }
        );
        var planLimits = StubProxy<ITenantPlanLimitsStore>.Create(
            (method, _) =>
                method.Name == nameof(ITenantPlanLimitsStore.GetAsync)
                    ? (true, Task.FromResult<TenantPlanLimits?>(null))
                    : (false, null)
        );

        var result = await GetMeHandler.Handle(
            new GetMeQuery(user.Id),
            AvatarDoubles.Users(user),
            tenants,
            roles,
            planLimits,
            StubProxy<IInvitationRepository>.Create((_, _) => (false, null)),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }
}
