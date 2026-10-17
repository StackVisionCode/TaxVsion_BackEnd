using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Infrastructure.Onboarding.Concurrency;

// Reemplazo distribuido del SemaphoreSlim para prod multi-pod. sp_getapplock @LockOwner='Session' se
// adhiere a la conexión que lo adquirió. Timeout 10s: si otro pod tiene el lock más tiempo, fallamos
// rápido con Onboarding.ReconcileLockBusy en vez de bloquear el request HTTP indefinidamente.
//
// Importante: abrimos una SqlConnection PROPIA (clonando el connection string del DbContext) en vez
// de usar la del DbContext. Si usáramos la del contexto, un SqlCommand sin .Transaction asignada
// falla cuando EF ya tiene una transacción local pendiente ("BeginExecuteNonQuery requires the
// command to have a transaction when the connection assigned to the command is in a pending local
// transaction"). Con conexión propia, el lock vive aislado hasta Dispose y al cerrar la conexión el
// motor libera el app lock automáticamente.
public sealed class SqlOnboardingReconcileSerializer(AuthDbContext db) : IOnboardingReconcileSerializer
{
    private const int LockTimeoutMilliseconds = 10_000;

    public async Task<IAsyncDisposable> AcquireAsync(Guid onboardingId, CancellationToken ct)
    {
        var resource = $"onboarding:{onboardingId:N}";
        var connectionString =
            db.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "Onboarding.ReconcileLockBusy: AuthDbContext has no connection string."
            );

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
            var returnCode = (int)result.Value!;
            if (returnCode < 0)
            {
                await connection.DisposeAsync();
                throw new InvalidOperationException(
                    $"Onboarding.ReconcileLockBusy: sp_getapplock returned {returnCode} for {resource}."
                );
            }

            return new SessionLockRelease(connection, resource);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class SessionLockRelease(SqlConnection connection, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    await using var release = connection.CreateCommand();
                    release.CommandText = "sp_releaseapplock";
                    release.CommandType = System.Data.CommandType.StoredProcedure;
                    release.Parameters.AddWithValue("@Resource", resource);
                    release.Parameters.AddWithValue("@LockOwner", "Session");
                    await release.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
