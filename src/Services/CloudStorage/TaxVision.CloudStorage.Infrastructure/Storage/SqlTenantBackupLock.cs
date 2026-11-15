using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>
/// Try-lock de SQL Server (<c>sp_getapplock</c> de sesión, timeout 0) por oficina para el backup programado: con
/// varias réplicas, solo una corre el backup de una oficina a la vez. Mismo molde que <see cref="SqlStuckScanRescueLock"/>.
/// </summary>
public sealed class SqlTenantBackupLock(CloudStorageDbContext db)
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid tenantId, CancellationToken ct)
    {
        var connectionString =
            db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The backup lock requires a database connection.");
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "sp_getapplock";
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource", $"cloudstorage:backup:{tenantId:N}");
            command.Parameters.AddWithValue("@LockMode", "Exclusive");
            command.Parameters.AddWithValue("@LockOwner", "Session");
            command.Parameters.AddWithValue("@LockTimeout", 0);
            var result = command.Parameters.Add("@Result", SqlDbType.Int);
            result.Direction = ParameterDirection.ReturnValue;
            await command.ExecuteNonQueryAsync(ct);

            if ((int)result.Value! < 0)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Release(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    // Cerrar la conexión libera el lock de sesión.
    private sealed class Release(SqlConnection connection) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
