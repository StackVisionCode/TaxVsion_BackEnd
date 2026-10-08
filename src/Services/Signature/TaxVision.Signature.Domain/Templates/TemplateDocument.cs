using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Templates;

public sealed class TemplateDocument : BaseEntity
{
    public const int MaxTitleLength = 200;

    private TemplateDocument() { }

    public Guid SignatureTemplateId { get; private set; }
    public int Order { get; private set; }
    public Guid FileId { get; private set; }
    public string Title { get; private set; } = default!;

    internal static Result<TemplateDocument> Create(Guid templateId, int order, Guid fileId, string title)
    {
        if (templateId == Guid.Empty || fileId == Guid.Empty)
            return Result.Failure<TemplateDocument>(
                new Error("Signature.TemplateDocument.Reference", "TemplateId and FileId are required.")
            );
        if (order < 1)
            return Result.Failure<TemplateDocument>(
                new Error("Signature.TemplateDocument.Order", "Document order must be at least 1.")
            );
        var normalizedTitle = title?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle) || normalizedTitle.Length > MaxTitleLength)
            return Result.Failure<TemplateDocument>(
                new Error(
                    "Signature.TemplateDocument.Title",
                    $"Title is required and cannot exceed {MaxTitleLength} characters."
                )
            );

        return Result.Success(
            new TemplateDocument
            {
                Id = Guid.NewGuid(),
                SignatureTemplateId = templateId,
                Order = order,
                FileId = fileId,
                Title = normalizedTitle,
            }
        );
    }

    internal void Reorder(int order) => Order = order;
}
