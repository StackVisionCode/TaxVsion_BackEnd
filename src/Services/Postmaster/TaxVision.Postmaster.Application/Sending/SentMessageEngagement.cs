namespace TaxVision.Postmaster.Application.Sending;

/// <summary>Filtro de "Email activity" por interacción: algún destinatario abrió o hizo clic.</summary>
public enum SentMessageEngagement
{
    Opened,
    Clicked,
}

/// <summary>Nombres que viajan en <c>?engagement=</c>; explícitos para que "0" o un nombre nuevo no se cuelen.</summary>
public static class SentMessageEngagementNames
{
    private static readonly Dictionary<string, SentMessageEngagement> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(SentMessageEngagement.Opened)] = SentMessageEngagement.Opened,
        [nameof(SentMessageEngagement.Clicked)] = SentMessageEngagement.Clicked,
    };

    public static IReadOnlyCollection<string> All => ByName.Keys;

    public static bool TryParse(string value, out SentMessageEngagement engagement) =>
        ByName.TryGetValue(value.Trim(), out engagement);
}
