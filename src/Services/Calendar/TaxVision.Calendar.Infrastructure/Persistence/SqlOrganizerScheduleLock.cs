using System.Data;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;

namespace TaxVision.Calendar.Infrastructure.Persistence;

public sealed class OrganizerScheduleLockOptions
{
    public const string SectionName = "Calendar:OrganizerScheduleLock";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

// @LockOwner = 'Transaction': el motor lo libera al confirmar o revertir la transacción del handler.
public sealed class SqlOrganizerScheduleLock(CalendarDbContext db, IOptions<OrganizerScheduleLockOptions> options)
    : IOrganizerScheduleLock
{
    public async Task AcquireAsync(Guid tenantId, Guid organizerUserId, CancellationToken ct)
    {
        var transaction =
            db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The organizer schedule lock requires an open transaction.");

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@Resource", $"calendar:organizer-schedule:{tenantId:N}:{organizerUserId:N}");
        AddParameter(command, "@LockMode", "Exclusive");
        AddParameter(command, "@LockOwner", "Transaction");
        AddParameter(command, "@LockTimeout", (int)options.Value.Timeout.TotalMilliseconds);
        var result = AddParameter(command, "@Result", null);
        result.DbType = DbType.Int32;
        result.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(ct);
        if (result.Value is not int code || code < 0)
            throw new ConflictException(
                "Calendar.Appointment.ScheduleBusy",
                "Another appointment is being scheduled for this organizer. Try again."
            );
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
