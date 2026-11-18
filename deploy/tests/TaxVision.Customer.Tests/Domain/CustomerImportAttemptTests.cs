using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Tests.Domain;

/// <summary>
/// Un import interrumpido (reinicio del servicio, mensaje reentregado) se retoma donde quedó: sabe qué filas ya
/// aplicó y qué clientes creó o actualizó, para cerrar con todos ellos.
/// </summary>
public sealed class CustomerImportAttemptTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ImportStatus.Validating)]
    [InlineData(ImportStatus.Applying)]
    [InlineData(ImportStatus.Canceling)]
    public void An_import_in_progress_can_be_resumed(ImportStatus status)
    {
        var attempt = InStatus(status);

        var resumed = attempt.Resume(Now);

        Assert.True(resumed.IsSuccess);
        Assert.Equal(status, attempt.Status);
        Assert.Equal(Now, attempt.LastProgressAtUtc);
    }

    [Fact]
    public void A_queued_or_finished_import_is_not_resumed()
    {
        Assert.True(NewAttempt().Resume(Now).IsFailure);

        var done = InStatus(ImportStatus.Applying);
        done.Complete();
        Assert.True(done.Resume(Now).IsFailure);
    }

    [Fact]
    public void It_knows_which_rows_it_already_applied_and_the_clients_they_touched()
    {
        var attempt = InStatus(ImportStatus.Applying);
        var created = Guid.NewGuid();
        var updated = Guid.NewGuid();
        attempt.RecordSuccess(2, created, "Ada");
        attempt.RecordUpdated(3, updated, "Grace", "Email");
        attempt.RecordSkipped(4, Guid.NewGuid(), "Linus", "Email");
        attempt.RecordFailed(5, "Bad", "Row.Email", "Email is required.");

        Assert.True(attempt.IsRowRecorded(2));
        Assert.True(attempt.IsRowRecorded(5));
        Assert.False(attempt.IsRowRecorded(6));
        Assert.Equal([created], attempt.CreatedCustomerIds);
        Assert.Equal([updated], attempt.UpdatedCustomerIds);
    }

    [Fact]
    public void Moving_to_applying_twice_is_harmless()
    {
        var attempt = InStatus(ImportStatus.Applying);

        Assert.True(attempt.MoveToApplying().IsSuccess);
        Assert.Equal(ImportStatus.Applying, attempt.Status);
    }

    [Fact]
    public void An_import_without_progress_for_too_long_is_stuck()
    {
        var attempt = InStatus(ImportStatus.Applying);
        attempt.Resume(Now);

        Assert.False(attempt.IsStuck(Now.AddMinutes(10), TimeSpan.FromMinutes(15)));
        Assert.True(attempt.IsStuck(Now.AddMinutes(16), TimeSpan.FromMinutes(15)));
        Assert.False(NewAttempt().IsStuck(Now.AddDays(1), TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void Each_recovery_is_counted_and_restarts_the_idle_clock()
    {
        var attempt = InStatus(ImportStatus.Applying);

        attempt.MarkRecoveryRequested(Now);
        attempt.MarkRecoveryRequested(Now.AddMinutes(20));

        Assert.Equal(2, attempt.RecoveryCount);
        Assert.Equal(Now.AddMinutes(20), attempt.LastProgressAtUtc);
    }

    // "Importado con avisos": la fila sigue creada o actualizada y guarda qué dato se dejó vacío.
    [Fact]
    public void Imported_rows_keep_their_warnings_and_are_counted()
    {
        var attempt = InStatus(ImportStatus.Applying);

        attempt.RecordSuccess(2, Guid.NewGuid(), "Ada", ["Phone '12' is not valid; imported without it."]);
        attempt.RecordUpdated(3, Guid.NewGuid(), "Grace", "Email", ["Occupation 'X' is not in the catalog."]);
        attempt.RecordSuccess(4, Guid.NewGuid(), "Alan");

        var ada = attempt.Rows.Single(r => r.RowNumber == 2);
        Assert.Equal(RowStatus.Success, ada.Status);
        Assert.Equal("Phone '12' is not valid; imported without it.", ada.Warnings);
        Assert.Equal(RowStatus.Updated, attempt.Rows.Single(r => r.RowNumber == 3).Status);
        Assert.Null(attempt.Rows.Single(r => r.RowNumber == 4).Warnings);
        Assert.Equal(2, attempt.WarningCount);
        Assert.Equal(2, attempt.SuccessCount);
    }

    // Merge u Overwrite sin nada nuevo: la fila no cuenta como actualizada.
    [Fact]
    public void A_matched_row_with_nothing_new_is_skipped_with_its_reason()
    {
        var attempt = InStatus(ImportStatus.Applying);
        var existing = Guid.NewGuid();

        attempt.RecordUnchanged(2, existing, "Ana", "TaxIdentifier", ["Occupation 'X' is not in the catalog."]);

        var row = attempt.Rows.Single(r => r.RowNumber == 2);
        Assert.Equal(RowStatus.Skipped, row.Status);
        Assert.Equal("No changes: the client already has this information.", row.Message);
        Assert.Equal("TaxIdentifier", row.MatchedBy);
        Assert.Equal(1, attempt.SkippedCount);
        Assert.Equal(0, attempt.UpdatedCount);
        Assert.Equal(1, attempt.WarningCount);
        Assert.Empty(attempt.UpdatedCustomerIds);
    }

    private static CustomerImportAttempt NewAttempt() =>
        CustomerImportAttempt
            .Create(Guid.NewGuid(), Guid.NewGuid(), "key", DuplicateStrategy.Skip, ImportSourceKind.Csv, "clients.csv")
            .Value;

    private static CustomerImportAttempt InStatus(ImportStatus status)
    {
        var attempt = NewAttempt();
        attempt.Start();
        if (status is ImportStatus.Applying or ImportStatus.Canceling)
            attempt.MoveToApplying();
        if (status == ImportStatus.Canceling)
            attempt.RequestCancel(attempt.CreatedByUserId);
        return attempt;
    }
}
