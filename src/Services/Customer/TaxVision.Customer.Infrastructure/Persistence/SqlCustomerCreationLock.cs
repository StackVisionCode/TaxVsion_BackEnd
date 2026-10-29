using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FromForms;

namespace TaxVision.Customer.Infrastructure.Persistence;

// @LockOwner = 'Transaction': el motor lo libera al confirmar o revertir la transacción del consumer.
public sealed class SqlCustomerCreationLock(CustomerDbContext db) : ICustomerCreationLock
{
    private const int LockTimeoutMilliseconds = 10_000;

    public async Task AcquireAsync(Guid tenantId, string normalizedEmail, CancellationToken ct)
    {
        var transaction =
            db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The customer creation lock requires an open transaction.");

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@Resource", CustomerCreationLockResource.For(tenantId, normalizedEmail));
        AddParameter(command, "@LockMode", "Exclusive");
        AddParameter(command, "@LockOwner", "Transaction");
        AddParameter(command, "@LockTimeout", LockTimeoutMilliseconds);
        var result = AddParameter(command, "@Result", null);
        result.DbType = DbType.Int32;
        result.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(ct);
        if (result.Value is not int code || code < 0)
            throw new TimeoutException("The customer creation lock is busy.");
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
