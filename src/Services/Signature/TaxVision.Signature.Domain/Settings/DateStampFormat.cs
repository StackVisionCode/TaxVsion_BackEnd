namespace TaxVision.Signature.Domain.Settings;

/// <summary>Cómo se escribe la fecha de firma en los campos Date del PDF sellado (lo elige la oficina).</summary>
public enum DateStampFormat
{
    /// <summary>MM/dd/yyyy — la costumbre de EE. UU. (default).</summary>
    MonthDayYear,

    /// <summary>dd/MM/yyyy.</summary>
    DayMonthYear,

    /// <summary>yyyy-MM-dd (ISO 8601).</summary>
    IsoDate,
}

public static class DateStampFormats
{
    public static string Pattern(this DateStampFormat format) =>
        format switch
        {
            DateStampFormat.DayMonthYear => "dd/MM/yyyy",
            DateStampFormat.IsoDate => "yyyy-MM-dd",
            _ => "MM/dd/yyyy",
        };
}
