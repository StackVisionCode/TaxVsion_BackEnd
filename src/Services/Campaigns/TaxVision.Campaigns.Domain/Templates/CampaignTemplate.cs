using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Domain.Templates;

/// <summary>
/// Plantilla de campaña REUTILIZABLE (multicanal, tenant-owned). Guarda contenido por canal (Email/SMS/
/// Push/…) que luego se aplica a una campaña nueva ("empezar desde plantilla") o se crea desde una
/// ("guardar como plantilla"). Es solo contenido — no tiene audiencia, remitente ni envío. SIN dinero.
/// </summary>
public sealed class CampaignTemplate : TenantEntity
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxSubjectLength = 300;
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 500_000;

    private readonly List<CampaignTemplateContent> _contents = [];

    private CampaignTemplate() { }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public CampaignChannel Channels { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<CampaignTemplateContent> Contents => _contents.AsReadOnly();

    public static Result<CampaignTemplate> Create(
        Guid tenantId,
        Guid createdByUserId,
        string name,
        string? description,
        CampaignChannel channels
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CampaignTemplate>(CampaignTemplateErrors.TenantRequired);
        if (createdByUserId == Guid.Empty)
            return Result.Failure<CampaignTemplate>(CampaignTemplateErrors.CreatedByRequired);

        var nameResult = NormalizeName(name);
        if (nameResult.IsFailure)
            return Result.Failure<CampaignTemplate>(nameResult.Error);
        var descResult = NormalizeDescription(description);
        if (descResult.IsFailure)
            return Result.Failure<CampaignTemplate>(descResult.Error);
        if (channels == CampaignChannel.None)
            return Result.Failure<CampaignTemplate>(CampaignTemplateErrors.ChannelsRequired);

        var now = DateTime.UtcNow;
        var template = new CampaignTemplate
        {
            Id = Guid.NewGuid(),
            Name = nameResult.Value,
            Description = descResult.Value,
            CreatedByUserId = createdByUserId,
            Channels = channels,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        template.SetTenant(tenantId);
        return Result.Success(template);
    }

    public Result Update(string name, string? description, CampaignChannel channels)
    {
        var nameResult = NormalizeName(name);
        if (nameResult.IsFailure)
            return nameResult;
        var descResult = NormalizeDescription(description);
        if (descResult.IsFailure)
            return Result.Failure(descResult.Error);
        if (channels == CampaignChannel.None)
            return Result.Failure(CampaignTemplateErrors.ChannelsRequired);

        Name = nameResult.Value;
        Description = descResult.Value;
        Channels = channels;
        // Purga contenido de canales ya no seleccionados.
        foreach (var existing in _contents.ToList())
            if (!Channels.HasFlag(existing.Channel))
                _contents.Remove(existing);
        Touch();
        return Result.Success();
    }

    public Result SetChannelContent(CampaignChannel channel, string? subject, string? title, string body)
    {
        if (channel == CampaignChannel.None || (channel & (channel - 1)) != 0)
            return Result.Failure(CampaignTemplateErrors.ContentChannelInvalid);
        if (!Channels.HasFlag(channel))
            return Result.Failure(CampaignTemplateErrors.ContentChannelNotSelected);

        var normalized = NormalizeContent(subject, title, body);
        if (normalized.IsFailure)
            return Result.Failure(normalized.Error);

        var (nSubject, nTitle, nBody) = normalized.Value;
        var existing = _contents.Find(c => c.Channel == channel);
        if (existing is null)
            _contents.Add(new CampaignTemplateContent(Id, TenantId, channel, nSubject, nTitle, nBody));
        else
            existing.Update(nSubject, nTitle, nBody);
        Touch();
        return Result.Success();
    }

    private static Result<string> NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<string>(CampaignTemplateErrors.NameRequired);
        var trimmed = name.Trim();
        return trimmed.Length > MaxNameLength
            ? Result.Failure<string>(CampaignTemplateErrors.NameTooLong)
            : Result.Success(trimmed);
    }

    private static Result<string?> NormalizeDescription(string? description)
    {
        var trimmed = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return trimmed is { Length: > MaxDescriptionLength }
            ? Result.Failure<string?>(CampaignTemplateErrors.DescriptionTooLong)
            : Result.Success(trimmed);
    }

    private static Result<(string? Subject, string? Title, string Body)> NormalizeContent(
        string? subject,
        string? title,
        string body
    )
    {
        if (string.IsNullOrWhiteSpace(body))
            return Result.Failure<(string?, string?, string)>(CampaignTemplateErrors.BodyRequired);
        if (body.Length > MaxBodyLength)
            return Result.Failure<(string?, string?, string)>(CampaignTemplateErrors.BodyTooLong);
        var nSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        if (nSubject is { Length: > MaxSubjectLength })
            return Result.Failure<(string?, string?, string)>(CampaignTemplateErrors.SubjectTooLong);
        var nTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (nTitle is { Length: > MaxTitleLength })
            return Result.Failure<(string?, string?, string)>(CampaignTemplateErrors.TitleTooLong);
        return Result.Success((nSubject, nTitle, body));
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
