namespace BuildingBlocks.Permissions;

/// <summary>
/// Opción B — persiste localmente un <see cref="RemotePermissionsSnapshot"/> recién recuperado de
/// Auth vía <see cref="IPermissionsSnapshotClient"/>, para que la próxima consulta ya encuentre fila
/// en la proyección (sin volver a pegarle a Auth cada request — el cache de 30s de
/// <c>ProjectionPermissionsSource</c> igual cubre el hot path inmediato). La implementación
/// concreta de cada servicio es un wrapper angosto sobre su propio
/// <c>IUserPermissionsProjectionRepository</c> local (upsert idempotente, mismo
/// <c>ApplyIfNewer</c> que ya usan los consumers de <c>UserRolesChangedIntegrationEvent</c>).
/// </summary>
public interface IUserPermissionsProjectionWriter
{
    Task PersistSnapshotAsync(
        Guid tenantId,
        Guid userId,
        RemotePermissionsSnapshot snapshot,
        CancellationToken ct = default
    );
}
