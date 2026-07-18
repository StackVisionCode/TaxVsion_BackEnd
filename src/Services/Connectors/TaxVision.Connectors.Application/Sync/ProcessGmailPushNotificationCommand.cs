namespace TaxVision.Connectors.Application.Sync;

/// <summary>HistoryId es el que trae el propio push — solo se usa como fallback si por algún motivo no hay ProviderWatchSubscription persistida para sembrar el cursor inicial (ver Handler).</summary>
public sealed record ProcessGmailPushNotificationCommand(string EmailAddress, string HistoryId);
