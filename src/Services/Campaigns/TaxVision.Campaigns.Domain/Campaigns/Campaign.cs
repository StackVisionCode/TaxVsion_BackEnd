using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Campaigns;

/// <summary>
/// Aggregate root de la <b>definición</b> de una campaña (orquestador agnóstico de canal, SIN
/// dinero — <c>documents/architecture/campaigns/campaigns/Domain_Design.md</c>). Estados:
/// <c>Draft → Ready → Scheduled → Archived</c>. Editable solo en <c>Draft</c>. No envía, no
/// materializa audiencia, no toca saldo — eso vive en <c>CampaignRun</c> y los ejecutores (fases
/// posteriores). Cada transición tiene su método explícito y devuelve <see cref="Result"/> (sin
/// <c>ChangeStatus(x)</c> genérico).
/// </summary>
public sealed class Campaign : TenantEntity
{
    public const int MaxNameLength = 200;

    private readonly List<CampaignSenderSelection> _senders = [];
    private readonly List<CampaignContent> _contents = [];

    private Campaign() { }

    public const int MaxSubjectLength = 300;
    public const int MaxTitleLength = 200;
    // Generoso para permitir HTML de email con imágenes embebidas (dataURL). La columna ya es nvarchar(max).
    public const int MaxMessageLength = 500_000;

    public string Name { get; private set; } = default!;
    public Guid CreatedByUserId { get; private set; }
    public CampaignChannel Channels { get; private set; }

    /// <summary>Asunto (Email). Opcional; para SMS/Push no aplica.</summary>
    public string? Subject { get; private set; }

    /// <summary>Cuerpo/mensaje del contenido (HTML/texto para Email, texto para SMS). Contenido mínimo del slice.</summary>
    public string Message { get; private set; } = default!;
    public CampaignStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Remitente seleccionado por canal (0..1 por canal). La campaña solo referencia el <c>SenderProfile</c> por id.</summary>
    public IReadOnlyCollection<CampaignSenderSelection> Senders => _senders.AsReadOnly();

    /// <summary>Contenido por canal (0..1 por canal). Si un canal no tiene contenido propio, el dispatch cae al <c>Message</c>/<c>Subject</c> base.</summary>
    public IReadOnlyCollection<CampaignContent> Contents => _contents.AsReadOnly();

    // ------------------------------------------------------------------
    // Factory
    // ------------------------------------------------------------------

    public static Result<Campaign> Create(
        Guid tenantId,
        Guid createdByUserId,
        string name,
        CampaignChannel channels,
        string message,
        string? subject
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Campaign>(CampaignErrors.TenantRequired);
        if (createdByUserId == Guid.Empty)
            return Result.Failure<Campaign>(CampaignErrors.CreatedByRequired);

        var nameResult = NormalizeName(name);
        if (nameResult.IsFailure)
            return Result.Failure<Campaign>(nameResult.Error);

        if (channels == CampaignChannel.None)
            return Result.Failure<Campaign>(CampaignErrors.ChannelsRequired);

        var contentResult = NormalizeContent(message, subject);
        if (contentResult.IsFailure)
            return Result.Failure<Campaign>(contentResult.Error);

        var now = DateTime.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = nameResult.Value,
            CreatedByUserId = createdByUserId,
            Channels = channels,
            Message = contentResult.Value.Message,
            Subject = contentResult.Value.Subject,
            Status = CampaignStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        campaign.SetTenant(tenantId);
        return Result.Success(campaign);
    }

    // ------------------------------------------------------------------
    // Edición (solo en Draft)
    // ------------------------------------------------------------------

    public Result Rename(string name)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        var nameResult = NormalizeName(name);
        if (nameResult.IsFailure)
            return nameResult;

        Name = nameResult.Value;
        Touch();
        return Result.Success();
    }

    public Result SetChannels(CampaignChannel channels)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        if (channels == CampaignChannel.None)
            return Result.Failure(CampaignErrors.ChannelsRequired);

        Channels = channels;
        Touch();
        return Result.Success();
    }

    public Result SetContent(string message, string? subject)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        var contentResult = NormalizeContent(message, subject);
        if (contentResult.IsFailure)
            return Result.Failure(contentResult.Error);

        Message = contentResult.Value.Message;
        Subject = contentResult.Value.Subject;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Selecciona (upsert) el remitente para un canal — solo en <c>Draft</c>. El canal debe ser único y
    /// estar entre los canales de la campaña. La validación de que el <c>SenderProfile</c> existe, es del
    /// tenant, está Active y es de ese canal la hace el handler (necesita el repo).
    /// </summary>
    public Result SetSender(CampaignChannel channel, Guid senderProfileId)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        if (channel == CampaignChannel.None || (channel & (channel - 1)) != 0)
            return Result.Failure(CampaignErrors.ChannelsRequired);
        if (!Channels.HasFlag(channel))
            return Result.Failure(CampaignErrors.SenderChannelNotSelected);
        if (senderProfileId == Guid.Empty)
            return Result.Failure(CampaignErrors.SenderRequired);

        var existing = _senders.Find(s => s.Channel == channel);
        if (existing is null)
            _senders.Add(new CampaignSenderSelection(Id, TenantId, channel, senderProfileId));
        else
            existing.Point(senderProfileId);

        Touch();
        return Result.Success();
    }

    public Result ClearSender(CampaignChannel channel)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        var existing = _senders.Find(s => s.Channel == channel);
        if (existing is not null)
        {
            _senders.Remove(existing);
            Touch();
        }
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Contenido por canal (override multicanal) — solo en Draft
    // ------------------------------------------------------------------

    /// <summary>
    /// Define (upsert) el contenido de UN canal. El canal debe ser un flag único y estar entre los canales
    /// de la campaña. <paramref name="subject"/> aplica a Email, <paramref name="title"/> a Push; el
    /// <paramref name="body"/> es obligatorio para todos. Solo en <c>Draft</c>.
    /// </summary>
    public Result SetChannelContent(CampaignChannel channel, string? subject, string? title, string body)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        if (channel == CampaignChannel.None || (channel & (channel - 1)) != 0)
            return Result.Failure(CampaignErrors.ContentChannelInvalid);
        if (!Channels.HasFlag(channel))
            return Result.Failure(CampaignErrors.ContentChannelNotSelected);

        var normalized = NormalizeChannelContent(subject, title, body);
        if (normalized.IsFailure)
            return Result.Failure(normalized.Error);

        var (nSubject, nTitle, nBody) = normalized.Value;
        var existing = _contents.Find(c => c.Channel == channel);
        if (existing is null)
            _contents.Add(new CampaignContent(Id, TenantId, channel, nSubject, nTitle, nBody));
        else
            existing.Update(nSubject, nTitle, nBody);

        Touch();
        return Result.Success();
    }

    public Result ClearChannelContent(CampaignChannel channel)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        var existing = _contents.Find(c => c.Channel == channel);
        if (existing is not null)
        {
            _contents.Remove(existing);
            Touch();
        }
        return Result.Success();
    }

    /// <summary>Contenido propio del canal, o <c>null</c> si el canal cae al <c>Message</c>/<c>Subject</c> base.</summary>
    public CampaignContent? GetContentFor(CampaignChannel channel) => _contents.Find(c => c.Channel == channel);

    // ------------------------------------------------------------------
    // Ciclo de vida
    // ------------------------------------------------------------------

    /// <summary>Draft → Ready. Revalida que la definición esté completa (por ahora: nombre + ≥1 canal).</summary>
    public Result MarkReady()
    {
        if (Status == CampaignStatus.Archived)
            return Result.Failure(CampaignErrors.Archived);
        if (Status != CampaignStatus.Draft)
            return Result.Failure(CampaignErrors.InvalidTransition);
        if (Channels == CampaignChannel.None)
            return Result.Failure(CampaignErrors.ChannelsRequired);

        Status = CampaignStatus.Ready;
        Touch();
        return Result.Success();
    }

    /// <summary>Ready/Scheduled → Draft, para volver a editar. No aplica a Archived.</summary>
    public Result RevertToDraft()
    {
        if (Status == CampaignStatus.Archived)
            return Result.Failure(CampaignErrors.Archived);
        if (Status == CampaignStatus.Draft)
            return Result.Success();

        Status = CampaignStatus.Draft;
        Touch();
        return Result.Success();
    }

    public Result Archive()
    {
        if (Status == CampaignStatus.Archived)
            return Result.Failure(CampaignErrors.InvalidTransition);

        Status = CampaignStatus.Archived;
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private Result EnsureDraft()
    {
        if (Status == CampaignStatus.Archived)
            return Result.Failure(CampaignErrors.Archived);
        return Status == CampaignStatus.Draft ? Result.Success() : Result.Failure(CampaignErrors.NotDraft);
    }

    private static Result<string> NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<string>(CampaignErrors.NameRequired);

        var trimmed = name.Trim();
        return trimmed.Length > MaxNameLength
            ? Result.Failure<string>(CampaignErrors.NameTooLong)
            : Result.Success(trimmed);
    }

    private static Result<(string Message, string? Subject)> NormalizeContent(string message, string? subject)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Result.Failure<(string, string?)>(CampaignErrors.MessageRequired);
        if (message.Length > MaxMessageLength)
            return Result.Failure<(string, string?)>(CampaignErrors.MessageTooLong);

        var normalizedSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        if (normalizedSubject is { Length: > MaxSubjectLength })
            return Result.Failure<(string, string?)>(CampaignErrors.SubjectTooLong);

        return Result.Success((message, normalizedSubject));
    }

    private static Result<(string? Subject, string? Title, string Body)> NormalizeChannelContent(
        string? subject,
        string? title,
        string body
    )
    {
        if (string.IsNullOrWhiteSpace(body))
            return Result.Failure<(string?, string?, string)>(CampaignErrors.MessageRequired);
        if (body.Length > MaxMessageLength)
            return Result.Failure<(string?, string?, string)>(CampaignErrors.MessageTooLong);

        var nSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        if (nSubject is { Length: > MaxSubjectLength })
            return Result.Failure<(string?, string?, string)>(CampaignErrors.SubjectTooLong);

        var nTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (nTitle is { Length: > MaxTitleLength })
            return Result.Failure<(string?, string?, string)>(CampaignErrors.TitleTooLong);

        return Result.Success((nSubject, nTitle, body));
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
