using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Persistence;

public sealed class DeliveryIssuanceLockOptions
{
    public const string SectionName = "Signature:DeliveryIssuanceLock";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Candado de SQL Server por solicitud, propio de la entrega: no comparte recurso con el de la cadena de auditoría.
/// <c>@LockOwner = 'Transaction'</c>: el motor lo suelta al confirmar o revertir.
/// </summary>
public sealed class SqlDeliveryIssuanceLock(SignatureDbContext db, IOptions<DeliveryIssuanceLockOptions> options)
    : IDeliveryIssuanceLock
{
    public async Task AcquireAsync(Guid signatureRequestId, CancellationToken ct = default)
    {
        var transaction =
            db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The delivery issuance lock requires an open transaction.");

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@Resource", $"signature-delivery:{signatureRequestId:N}");
        AddParameter(command, "@LockMode", "Exclusive");
        AddParameter(command, "@LockOwner", "Transaction");
        AddParameter(command, "@LockTimeout", (int)options.Value.Timeout.TotalMilliseconds);
        var result = AddParameter(command, "@Result", null);
        result.DbType = DbType.Int32;
        result.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(ct);
        // Transitoria: el reintento estándar de Wolverine vuelve a intentar.
        if (result.Value is not int code || code < 0)
            throw new TimeoutException("The delivery issuance lock is busy.");
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
