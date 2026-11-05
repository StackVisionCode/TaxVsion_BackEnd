using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Infrastructure.Audit;

/// <summary>
/// SIG-4: candado de SQL Server por solicitud para la cadena de auditoría. Los appends de la misma solicitud
/// se ordenan (cada uno lee el tail que dejó el anterior) y los de otras solicitudes no se bloquean.
/// <c>@LockOwner = 'Transaction'</c>: el motor lo suelta al confirmar o revertir.
/// </summary>
public sealed class SqlAuditChainLock(SignatureDbContext db)
{
    private const int LockTimeoutMilliseconds = 15_000;

    public async Task AcquireAsync(Guid signatureRequestId, CancellationToken ct)
    {
        var transaction =
            db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The audit chain lock requires an open transaction.");

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@Resource", $"signature-audit:{signatureRequestId:N}");
        AddParameter(command, "@LockMode", "Exclusive");
        AddParameter(command, "@LockOwner", "Transaction");
        AddParameter(command, "@LockTimeout", LockTimeoutMilliseconds);
        var result = AddParameter(command, "@Result", null);
        result.DbType = DbType.Int32;
        result.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(ct);
        // Transitoria: el reintento estándar de Wolverine vuelve a intentar.
        if (result.Value is not int code || code < 0)
            throw new TimeoutException("The audit chain lock is busy.");
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
