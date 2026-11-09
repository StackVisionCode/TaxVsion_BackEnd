using System.Data;
using Microsoft.Data.SqlClient;

namespace TaxVision.Scribe.Infrastructure.Seed;

/// <summary>Candado entre réplicas para la siembra de arranque.</summary>
public interface ISeedLock
{
    /// <summary>Null = otra réplica lo tiene. Se libera al disponer el resultado.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken ct);
}

/// <summary>
/// SCR-02: <c>sp_getapplock</c> de sesión en una conexión propia, sin espera. La siembra dura minutos y sube
/// archivos, así que no puede vivir en una transacción. Si el proceso muere, el motor suelta el candado.
/// </summary>
public sealed class SqlSeedLock(string connectionString) : ISeedLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Resource", resource);
        command.Parameters.AddWithValue("@LockMode", "Exclusive");
        command.Parameters.AddWithValue("@LockOwner", "Session");
        command.Parameters.AddWithValue("@LockTimeout", 0);
        var result = command.Parameters.Add("@Result", SqlDbType.Int);
        result.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(ct);
        if (result.Value is int code && code >= 0)
            return new HeldLock(connection, resource);

        await connection.DisposeAsync();
        return null;
    }

    // Con pooling, cerrar la conexión no termina la sesión: hay que soltarlo a mano.
    private sealed class HeldLock(SqlConnection connection, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using (connection)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "sp_releaseapplock";
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@Resource", resource);
                command.Parameters.AddWithValue("@LockOwner", "Session");
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
