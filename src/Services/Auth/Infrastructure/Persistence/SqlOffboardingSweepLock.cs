using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Offboarding;

namespace TaxVision.Auth.Infrastructure.Persistence;

/// <summary>
/// Try-lock de SQL Server (<c>sp_getapplock</c> de sesión, timeout 0) para el barrido del retiro: mismo molde
/// que <c>SqlSeatSlotRecomputeSerializer</c>, pero sin esperar — si otra réplica lo tiene, este tick se salta.
/// </summary>
public sealed class SqlOffboardingSweepLock(AuthDbContext db) : IOffboardingSweepLock
{
    private const string Resource = "offboarding-runs:sweep";

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken ct)
    {
        var connectionString =
            db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Offboarding sweep requires a database connection.");
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "sp_getapplock";
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource", Resource);
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

    private sealed class Release(SqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "sp_releaseapplock";
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@Resource", Resource);
                    command.Parameters.AddWithValue("@LockOwner", "Session");
                    await command.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
