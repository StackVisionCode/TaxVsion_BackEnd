namespace TaxVision.Postmaster.Application.Sending;

/// <summary>Filtros de "Email activity". <see cref="Status"/> usa los valores de <see cref="SentMessageActivityStatus"/>.</summary>
public sealed record SentMessageActivityFilter(string? Status, string? Recipient, DateTime? FromUtc, DateTime? ToUtc);
