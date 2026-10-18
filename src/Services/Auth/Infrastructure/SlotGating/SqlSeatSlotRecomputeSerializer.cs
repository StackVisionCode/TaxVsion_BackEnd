using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.SlotGating.Abstractions;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Infrastructure.SlotGating;

public sealed class SqlSeatSlotRecomputeSerializer(AuthDbContext db) : ISeatSlotRecomputeSerializer
{
    private const int LockTimeoutMilliseconds = 10_000;

    public async Task<IAsyncDisposable> AcquireAsync(Guid tenantId, CancellationToken ct)
    {
        var resource = $"seat-slots:{tenantId:N}";
        var connectionString =
            db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Seat slot projection requires a database connection.");
        var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "sp_getapplock";
            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource", resource);
            command.Parameters.AddWithValue("@LockMode", "Exclusive");
            command.Parameters.AddWithValue("@LockOwner", "Session");
            command.Parameters.AddWithValue("@LockTimeout", LockTimeoutMilliseconds);
            var result = command.Parameters.Add("@Result", System.Data.SqlDbType.Int);
            result.Direction = System.Data.ParameterDirection.ReturnValue;

            await command.ExecuteNonQueryAsync(ct);
            if ((int)result.Value! < 0)
                throw new InvalidOperationException("Seat slot recompute lock is busy.");

            return new Release(connection, resource);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Release(SqlConnection connection, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "sp_releaseapplock";
                    command.CommandType = System.Data.CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@Resource", resource);
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
