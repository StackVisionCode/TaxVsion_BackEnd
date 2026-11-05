using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Persistence;

// @LockOwner = 'Transaction': el motor lo libera al confirmar o revertir la transacción del handler.
public sealed class SqlTenantAdminLock(AuthDbContext db, IOptions<TenantAdminLockOptions> options) : ITenantAdminLock
{
    public async Task AcquireAsync(Guid tenantId, CancellationToken ct)
    {
        var transaction =
            db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The tenant admin lock requires an open transaction.");

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@Resource", $"auth-last-admin:{tenantId:N}");
        AddParameter(command, "@LockMode", "Exclusive");
        AddParameter(command, "@LockOwner", "Transaction");
        AddParameter(command, "@LockTimeout", options.Value.TimeoutMilliseconds);
        var result = AddParameter(command, "@Result", null);
        result.DbType = DbType.Int32;
        result.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(ct);
        if (result.Value is not int code || code < 0)
            throw new TimeoutException("The tenant admin lock is busy.");
    }

    private static IDbDataParameter AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }
}
