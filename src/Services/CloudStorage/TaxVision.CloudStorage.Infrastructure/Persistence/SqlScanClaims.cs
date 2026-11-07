using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Infrastructure.Persistence;

/// <summary>
/// CST-16 fuera de la transacción (fila L): <c>sp_getapplock</c> con dueño <c>Session</c> en una conexión propia que
/// vive lo que dura el escaneo. Sin pool: si el proceso cae, la conexión se cierra y SQL Server suelta el candado.
/// </summary>
public sealed class SqlScanClaims(CloudStorageDbContext db) : IScanClaims
{
    public async Task<IAsyncDisposable?> TryClaimAsync(Guid fileId, CancellationToken ct)
    {
        var connectionString = new SqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Pooling = false,
        }.ConnectionString;
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            // Timeout 0: quien no lo consigue no espera.
            var result = await ExecuteAsync(connection, "sp_getapplock", fileId, withMode: true, ct);
            if (result >= 0)
                return new ScanClaim(connection, fileId);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private static async Task<int> ExecuteAsync(
        SqlConnection connection,
        string procedure,
        Guid fileId,
        bool withMode,
        CancellationToken ct
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = procedure;
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@Resource", $"cloudstorage:scan:{fileId:N}"));
        command.Parameters.Add(new SqlParameter("@LockOwner", "Session"));
        if (withMode)
        {
            command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
            command.Parameters.Add(new SqlParameter("@LockTimeout", 0));
        }
        var result = new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
        command.Parameters.Add(result);
        await command.ExecuteNonQueryAsync(ct);
        return (int)result.Value!;
    }

    private sealed class ScanClaim(SqlConnection connection, Guid fileId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteAsync(connection, "sp_releaseapplock", fileId, withMode: false, CancellationToken.None);
            }
            finally
            {
                // Cerrar la sesión también lo suelta si la liberación falló.
                await connection.DisposeAsync();
            }
        }
    }
}
