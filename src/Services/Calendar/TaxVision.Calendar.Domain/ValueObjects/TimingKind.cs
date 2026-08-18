namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Las tres formas de existir en el tiempo. Confundirlas es el bug clasico.</summary>
public enum TimingKind
{
    PointInTime = 1,
    AllDay = 2,
    Recurring = 3,
}
