using System.Text.Json;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Application.Email.Accounts;

/// <summary>Proyección de una cuenta conectada. NUNCA incluye tokens/credenciales.</summary>
public sealed record EmailAccountResponse(
    Guid Id,
    Guid TenantId,
    Guid OwnerUserId,
    string Provider,
    string EmailAddress,
    string? DisplayName,
    string SyncStatus,
    DateTime? LastSyncAtUtc,
    DateTime? LastFullSyncAtUtc,
    bool IsActive,
    string? LastError,
    DateTime CreatedAtUtc
);

public sealed record EmailFolderResponse(
    Guid Id,
    string ExternalId,
    string Name,
    string Kind,
    int TotalMessages,
    DateTime? LastSyncAtUtc
);

public sealed record EmailMessageSummaryResponse(
    Guid Id,
    string ExternalMessageId,
    string? ExternalThreadId,
    string? Subject,
    string? FromAddress,
    string? Snippet,
    bool IsRead,
    bool IsStarred,
    bool HasAttachments,
    DateTime? ReceivedAtUtc
);

public sealed record EmailMessageDetailResponse(
    Guid Id,
    string ExternalMessageId,
    string? ExternalThreadId,
    string? Subject,
    string? FromAddress,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string? BodyHtml,
    string? BodyText,
    bool IsRead,
    bool IsStarred,
    DateTime? ReceivedAtUtc,
    DateTime? SentAtUtc
);

public sealed record EmailSyncLogResponse(
    Guid Id,
    string Type,
    string Status,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    int FoldersSynced,
    int MessagesSynced,
    string? Error
);

public static class EmailAccountMapper
{
    public static EmailAccountResponse ToResponse(EmailAccountConnection a) =>
        new(
            a.Id,
            a.TenantId,
            a.OwnerUserId,
            a.Provider.ToString(),
            a.EmailAddress,
            a.DisplayName,
            a.SyncStatus.ToString(),
            a.LastSyncAtUtc,
            a.LastFullSyncAtUtc,
            a.IsActive,
            a.LastError,
            a.CreatedAtUtc
        );

    public static EmailFolderResponse ToResponse(EmailFolder f) =>
        new(f.Id, f.ExternalId, f.Name, f.Kind.ToString(), f.TotalMessages, f.LastSyncAtUtc);

    public static EmailMessageSummaryResponse ToSummary(EmailSyncedMessage m) =>
        new(m.Id, m.ExternalMessageId, m.ExternalThreadId, m.Subject, m.FromAddress, m.Snippet, m.IsRead, m.IsStarred, m.HasAttachments, m.ReceivedAtUtc);

    public static EmailMessageDetailResponse ToDetail(EmailSyncedMessage m) =>
        new(
            m.Id,
            m.ExternalMessageId,
            m.ExternalThreadId,
            m.Subject,
            m.FromAddress,
            ParseList(m.ToJson),
            ParseList(m.CcJson),
            ParseList(m.BccJson),
            m.BodyHtml,
            m.BodyText,
            m.IsRead,
            m.IsStarred,
            m.ReceivedAtUtc,
            m.SentAtUtc
        );

    public static EmailSyncLogResponse ToResponse(EmailSyncLog l) =>
        new(l.Id, l.Type.ToString(), l.Status.ToString(), l.StartedAtUtc, l.FinishedAtUtc, l.FoldersSynced, l.MessagesSynced, l.Error);

    private static IReadOnlyList<string> ParseList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
