using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class ConflictDetectorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int hour, int minute = 0) => new(2026, 3, 10, hour, minute, 0, DateTimeKind.Utc);

    private static AppointmentType Type(bool blocksOnConflict) =>
        AppointmentType
            .Create(Tenant, "Revision", TimeSpan.FromHours(1), "#1A2B3C", Now, blocksOnConflict: blocksOnConflict)
            .Value;

    private static Occurrence Busy(int fromHour, int toHour) =>
        new(Guid.NewGuid(), Utc(fromHour), Utc(fromHour), Utc(toHour), false, "Otra cita", null);

    private static BlockedTime Vacation(int fromHour, int toHour) =>
        BlockedTime.Create(Tenant, Preparer, Utc(fromHour), Utc(toHour), "vacaciones", Now).Value;

    [Fact]
    public void An_overlap_on_a_permissive_type_is_only_a_warning()
    {
        var result = ConflictDetector.Check(Utc(10), Utc(11), Type(blocksOnConflict: false), [Busy(10, 11)], []);

        Assert.True(result.HasConflict);
        Assert.False(result.Blocks);
        Assert.Single(result.Conflicting);
    }

    [Fact]
    public void An_overlap_on_a_type_that_blocks_is_a_conflict()
    {
        // Una firma presencial no admite estar en dos sitios: ahi solapar es un error, no un aviso.
        var result = ConflictDetector.Check(Utc(10), Utc(11), Type(blocksOnConflict: true), [Busy(10, 11)], []);

        Assert.True(result.HasConflict);
        Assert.True(result.Blocks);
    }

    [Fact]
    public void A_blocked_time_always_blocks_whatever_the_type_says()
    {
        var permissive = ConflictDetector.Check(Utc(10), Utc(11), Type(blocksOnConflict: false), [], [Vacation(9, 12)]);

        Assert.True(permissive.HasConflict);
        Assert.True(permissive.Blocks);
    }

    [Fact]
    public void Touching_without_overlapping_is_not_a_conflict()
    {
        // Una cita de 10 a 11 y otra de 11 a 12 no chocan: el fin es exclusivo.
        var result = ConflictDetector.Check(Utc(11), Utc(12), Type(blocksOnConflict: true), [Busy(10, 11)], []);

        Assert.False(result.HasConflict);
        Assert.False(result.Blocks);
    }

    [Fact]
    public void The_free_slots_of_a_nine_to_five_with_lunch_and_one_appointment_are_exact()
    {
        // Horario 9-17, almuerzo 12-13 y una cita a las 10. Los huecos son 9-10, 11-12 y 13-17.
        var workday = new[] { new TimeSlot(Utc(9), Utc(17)) };

        var free = ConflictDetector.FreeSlots(
            Utc(0),
            Utc(23),
            workday,
            [Busy(10, 11)],
            [BlockedTime.Create(Tenant, Preparer, Utc(12), Utc(13), "almuerzo", Now).Value],
            TimeSpan.FromMinutes(15)
        );

        Assert.Equal(3, free.Count);
        Assert.Equal(new TimeSlot(Utc(9), Utc(10)), free[0]);
        Assert.Equal(new TimeSlot(Utc(11), Utc(12)), free[1]);
        Assert.Equal(new TimeSlot(Utc(13), Utc(17)), free[2]);
    }

    [Fact]
    public void A_gap_shorter_than_the_minimum_is_not_offered()
    {
        // Diez minutos entre dos citas no son un hueco: no cabe una consulta de treinta.
        var workday = new[] { new TimeSlot(Utc(9), Utc(12)) };

        var free = ConflictDetector.FreeSlots(
            Utc(0),
            Utc(23),
            workday,
            [Busy(9, 10), new Occurrence(Guid.NewGuid(), Utc(10, 10), Utc(10, 10), Utc(12), false, "x", null)],
            [],
            TimeSpan.FromMinutes(30)
        );

        Assert.Empty(free);
    }

    [Fact]
    public void Overlapping_busy_blocks_do_not_produce_negative_slots()
    {
        var workday = new[] { new TimeSlot(Utc(9), Utc(17)) };

        var free = ConflictDetector.FreeSlots(
            Utc(0),
            Utc(23),
            workday,
            [Busy(10, 14), Busy(11, 12)],
            [],
            TimeSpan.FromMinutes(15)
        );

        Assert.Equal(2, free.Count);
        Assert.Equal(new TimeSlot(Utc(9), Utc(10)), free[0]);
        Assert.Equal(new TimeSlot(Utc(14), Utc(17)), free[1]);
    }

    [Fact]
    public void A_free_slot_carries_no_information_about_the_appointments_behind_it()
    {
        var free = ConflictDetector.FreeSlots(
            Utc(0),
            Utc(23),
            [new TimeSlot(Utc(9), Utc(17))],
            [Busy(10, 11)],
            [],
            TimeSpan.FromMinutes(15)
        );

        // El tipo no tiene donde llevar un titulo: la fuga de la agenda ajena la impide el modelo, no
        // una decision del handler.
        var properties = typeof(TimeSlot).GetProperties();
        Assert.Equal(2, properties.Length);
        Assert.All(properties, p => Assert.Equal(typeof(DateTime), p.PropertyType));
        Assert.NotEmpty(free);
    }
}
