namespace TaxVision.Calendar.Infrastructure.Jobs;

public sealed class StartingSoonOptions
{
    public const string SectionName = "Calendar:StartingSoon";

    /// <summary>Cada cuánto corre el job.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Antelación del aviso. Se avisa entre <c>Lead</c> y <c>Lead + Interval</c> antes de empezar.</summary>
    public TimeSpan Lead { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Cuánto se guardan las marcas de aviso: pasado esto ya nadie puede volver a avisar esa ocurrencia.</summary>
    public TimeSpan NoticeRetention { get; set; } = TimeSpan.FromDays(7);
}
