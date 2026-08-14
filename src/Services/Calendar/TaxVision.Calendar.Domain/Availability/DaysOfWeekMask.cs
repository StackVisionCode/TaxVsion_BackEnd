namespace TaxVision.Calendar.Domain.Availability;

/// <summary>
/// Dias en que aplica una regla. Mascara y no una coleccion: cabe en una columna <c>int</c>, se filtra
/// en SQL y no necesita tabla hija.
/// </summary>
[Flags]
public enum DaysOfWeekMask
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,

    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    EveryDay = Weekdays | Saturday | Sunday,
}

public static class DaysOfWeekMaskExtensions
{
    public static bool Includes(this DaysOfWeekMask mask, DayOfWeek day) => (mask & From(day)) != DaysOfWeekMask.None;

    public static DaysOfWeekMask From(DayOfWeek day) =>
        day switch
        {
            DayOfWeek.Sunday => DaysOfWeekMask.Sunday,
            DayOfWeek.Monday => DaysOfWeekMask.Monday,
            DayOfWeek.Tuesday => DaysOfWeekMask.Tuesday,
            DayOfWeek.Wednesday => DaysOfWeekMask.Wednesday,
            DayOfWeek.Thursday => DaysOfWeekMask.Thursday,
            DayOfWeek.Friday => DaysOfWeekMask.Friday,
            _ => DaysOfWeekMask.Saturday,
        };
}
