using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Tests.Domain;

public sealed class CampaignScheduleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Campaign = Guid.NewGuid();

    [Fact]
    public void CreateRecurring_requires_positive_interval()
    {
        var result = CampaignSchedule.CreateRecurring(
            Tenant,
            Campaign,
            DateTime.UtcNow,
            RecurrenceFrequency.Custom,
            0,
            []
        );
        Assert.True(result.IsFailure);
        Assert.Equal(CampaignScheduleErrors.IntervalInvalid, result.Error);
    }

    [Fact]
    public void OneTime_MarkFired_completes_and_clears_next()
    {
        var schedule = CampaignSchedule.CreateOneTime(Tenant, Campaign, DateTime.UtcNow, []).Value;
        var runId = Guid.NewGuid();

        schedule.MarkFired(runId, DateTime.UtcNow);

        Assert.Equal(ScheduleStatus.Completed, schedule.Status);
        Assert.Null(schedule.NextFireAtUtc);
        Assert.Equal(runId, schedule.ActiveRunId);
    }

    [Fact]
    public void Recurring_MarkFired_coalesces_missed_slots_to_next_future()
    {
        // NextFire muy atrasado (servicio caído): al disparar, avanza al próximo slot FUTURO, no uno por cada perdido.
        var start = DateTime.UtcNow.AddMinutes(-100);
        var schedule = CampaignSchedule
            .CreateRecurring(Tenant, Campaign, start, RecurrenceFrequency.Custom, 10, [])
            .Value;

        var now = DateTime.UtcNow;
        schedule.MarkFired(Guid.NewGuid(), now);

        Assert.Equal(ScheduleStatus.Active, schedule.Status);
        Assert.NotNull(schedule.NextFireAtUtc);
        Assert.True(schedule.NextFireAtUtc > now, "next fire must be in the future (coalesced)");
    }

    [Fact]
    public void MarkFired_with_empty_runId_does_not_set_overlap_guard()
    {
        var schedule = CampaignSchedule
            .CreateRecurring(Tenant, Campaign, DateTime.UtcNow, RecurrenceFrequency.Custom, 10, [])
            .Value;
        schedule.MarkFired(Guid.Empty, DateTime.UtcNow);
        Assert.Null(schedule.ActiveRunId);
    }

    [Fact]
    public void ReleaseActiveRun_clears_only_matching_run()
    {
        var schedule = CampaignSchedule.CreateOneTime(Tenant, Campaign, DateTime.UtcNow, []).Value;
        var runId = Guid.NewGuid();
        schedule.MarkFired(runId, DateTime.UtcNow);

        schedule.ReleaseActiveRun(Guid.NewGuid()); // otro run: no-op
        Assert.Equal(runId, schedule.ActiveRunId);

        schedule.ReleaseActiveRun(runId);
        Assert.Null(schedule.ActiveRunId);
    }

    [Fact]
    public void IsClaimable_true_only_when_active_due_unleased_and_no_active_run()
    {
        var past = DateTime.UtcNow.AddMinutes(-1);
        var schedule = CampaignSchedule.CreateOneTime(Tenant, Campaign, past, []).Value;
        Assert.True(schedule.IsClaimable(DateTime.UtcNow));

        schedule.Pause();
        Assert.False(schedule.IsClaimable(DateTime.UtcNow));
    }

    [Fact]
    public void Pause_resume_cancel_transitions()
    {
        var schedule = CampaignSchedule
            .CreateRecurring(Tenant, Campaign, DateTime.UtcNow, RecurrenceFrequency.Custom, 10, [])
            .Value;

        Assert.True(schedule.Pause().IsSuccess);
        Assert.True(schedule.Pause().IsFailure); // ya no está Active
        Assert.True(schedule.Resume().IsSuccess);
        Assert.True(schedule.Cancel().IsSuccess);
        Assert.Equal(ScheduleStatus.Cancelled, schedule.Status);
        Assert.Null(schedule.NextFireAtUtc);
    }

    // ---------------------------------------------------------------------
    // Recurrencia: frecuencias nombradas + fin por fecha/cantidad
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(RecurrenceFrequency.Hourly, 60)] // +1h
    [InlineData(RecurrenceFrequency.Daily, 24 * 60)]
    [InlineData(RecurrenceFrequency.Weekly, 7 * 24 * 60)]
    public void Named_frequency_advances_by_calendar_step(RecurrenceFrequency freq, int expectedMinutes)
    {
        var start = DateTime.UtcNow.AddMinutes(1); // futuro cercano: un solo avance
        var schedule = CampaignSchedule.CreateRecurring(Tenant, Campaign, start, freq, null, []).Value;

        schedule.MarkFired(Guid.NewGuid(), start);

        Assert.Equal(ScheduleStatus.Active, schedule.Status);
        Assert.Equal(start.AddMinutes(expectedMinutes), schedule.NextFireAtUtc);
    }

    [Fact]
    public void Monthly_frequency_uses_calendar_month()
    {
        var start = new DateTime(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc);
        var schedule = CampaignSchedule
            .CreateRecurring(Tenant, Campaign, start, RecurrenceFrequency.Monthly, null, [])
            .Value;

        schedule.MarkFired(Guid.NewGuid(), start);

        // AddMonths hace clamp de fin de mes: 31-ene → 28-feb.
        Assert.Equal(new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc), schedule.NextFireAtUtc);
    }

    [Fact]
    public void Custom_frequency_requires_interval()
    {
        var result = CampaignSchedule.CreateRecurring(
            Tenant,
            Campaign,
            DateTime.UtcNow,
            RecurrenceFrequency.Custom,
            null,
            []
        );
        Assert.True(result.IsFailure);
        Assert.Equal(CampaignScheduleErrors.IntervalInvalid, result.Error);
    }

    [Fact]
    public void End_date_before_start_is_rejected()
    {
        var start = DateTime.UtcNow.AddDays(1);
        var result = CampaignSchedule.CreateRecurring(
            Tenant,
            Campaign,
            start,
            RecurrenceFrequency.Daily,
            null,
            [],
            endsAtUtc: start.AddHours(-1)
        );
        Assert.True(result.IsFailure);
        Assert.Equal(CampaignScheduleErrors.EndBeforeStart, result.Error);
    }

    [Fact]
    public void Non_positive_max_occurrences_is_rejected()
    {
        var result = CampaignSchedule.CreateRecurring(
            Tenant,
            Campaign,
            DateTime.UtcNow,
            RecurrenceFrequency.Daily,
            null,
            [],
            maxOccurrences: 0
        );
        Assert.True(result.IsFailure);
        Assert.Equal(CampaignScheduleErrors.MaxOccurrencesInvalid, result.Error);
    }

    [Fact]
    public void Max_occurrences_completes_after_last_fire()
    {
        var start = DateTime.UtcNow.AddMinutes(1);
        var schedule = CampaignSchedule
            .CreateRecurring(Tenant, Campaign, start, RecurrenceFrequency.Hourly, null, [], maxOccurrences: 2)
            .Value;

        schedule.MarkFired(Guid.NewGuid(), start); // 1er disparo
        Assert.Equal(ScheduleStatus.Active, schedule.Status);
        Assert.Equal(1, schedule.OccurrenceCount);
        Assert.NotNull(schedule.NextFireAtUtc);

        schedule.MarkFired(Guid.NewGuid(), schedule.NextFireAtUtc!.Value); // 2do: tope alcanzado
        Assert.Equal(ScheduleStatus.Completed, schedule.Status);
        Assert.Equal(2, schedule.OccurrenceCount);
        Assert.Null(schedule.NextFireAtUtc);
    }

    [Fact]
    public void End_date_completes_when_next_slot_passes_it()
    {
        var start = DateTime.UtcNow.AddMinutes(1);
        // Fin justo antes del 2do slot (start + ~1 día): tras el 1er disparo no quedan más.
        var schedule = CampaignSchedule
            .CreateRecurring(Tenant, Campaign, start, RecurrenceFrequency.Daily, null, [], endsAtUtc: start.AddHours(12))
            .Value;

        schedule.MarkFired(Guid.NewGuid(), start);

        Assert.Equal(ScheduleStatus.Completed, schedule.Status);
        Assert.Null(schedule.NextFireAtUtc);
    }

    [Fact]
    public void ContactListIds_roundtrips_from_csv()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var schedule = CampaignSchedule.CreateOneTime(Tenant, Campaign, DateTime.UtcNow, [a, b, a]).Value;
        var ids = schedule.ContactListIds();
        Assert.Equal(2, ids.Count); // deduped
        Assert.Contains(a, ids);
        Assert.Contains(b, ids);
    }

    // ---------------------------------------------------------------------
    // A1 — el schedule congela la visibilidad de quien lo agendó
    // ---------------------------------------------------------------------

    /// <summary>
    /// El scheduler dispara con un actor de sistema, así que corría con la visibilidad abierta: un
    /// preparador que solo ve sus clientes asignados agendaba una campaña y el envío salía a la cartera
    /// completa de la oficina. La decisión de audiencia se toma al agendar; el disparo solo la ejecuta.
    /// </summary>
    [Fact]
    public void A_schedule_remembers_who_created_it_and_what_that_person_could_see()
    {
        var creator = Guid.NewGuid();

        var schedule = CampaignSchedule
            .CreateRecurring(
                Tenant,
                Campaign,
                DateTime.UtcNow,
                RecurrenceFrequency.Custom,
                60,
                [],
                includeCustomers: true,
                createdByUserId: creator,
                creatorCanViewAllCustomers: false
            )
            .Value;

        Assert.Equal(creator, schedule.CreatedByUserId);
        Assert.False(schedule.CreatorCanViewAllCustomers);
    }

    /// <summary>
    /// §R.7 — un schedule creado antes de esta fase no sabe qué veía su creador. Asumir que veía POCO
    /// cambiaría a quién se le envía un correo ya agendado, en silencio: el default abierto conserva el
    /// comportamiento actual (y la migración usa el mismo default para las filas existentes).
    /// </summary>
    [Fact]
    public void A_schedule_without_a_recorded_creator_keeps_the_open_visibility()
    {
        var schedule = CampaignSchedule.CreateOneTime(Tenant, Campaign, DateTime.UtcNow, []).Value;

        Assert.Null(schedule.CreatedByUserId);
        Assert.True(schedule.CreatorCanViewAllCustomers);
    }
}
