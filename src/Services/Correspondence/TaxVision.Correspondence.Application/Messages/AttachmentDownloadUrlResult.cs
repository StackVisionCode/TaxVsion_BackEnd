namespace TaxVision.Correspondence.Application.Messages;

public sealed record AttachmentDownloadUrlResult(Guid AttachmentId, Uri DownloadUrl, DateTime ExpiresAtUtc);
