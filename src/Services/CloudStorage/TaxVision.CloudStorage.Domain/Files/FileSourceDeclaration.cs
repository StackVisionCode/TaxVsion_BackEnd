using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Files;

/// <summary>Recurso de negocio del que nace un archivo, tal como lo declara el servicio dueño.</summary>
public sealed partial record FileSourceDeclaration
{
    public const int ServiceMaxLength = 64;
    public const int ResourceTypeMaxLength = 64;
    public const int RoleMaxLength = 32;

    private FileSourceDeclaration(
        string service,
        string resourceType,
        Guid resourceId,
        string role,
        Guid? relatedCustomerId
    )
    {
        Service = service;
        ResourceType = resourceType;
        ResourceId = resourceId;
        Role = role;
        RelatedCustomerId = relatedCustomerId;
    }

    public string Service { get; }
    public string ResourceType { get; }
    public Guid ResourceId { get; }
    public string Role { get; }
    public Guid? RelatedCustomerId { get; }

    public static Result<FileSourceDeclaration> Create(
        string? service,
        string? resourceType,
        Guid resourceId,
        string? role,
        Guid? relatedCustomerId
    )
    {
        if (
            !IsCode(service, ServiceMaxLength)
            || !IsCode(resourceType, ResourceTypeMaxLength)
            || !IsCode(role, RoleMaxLength)
            || resourceId == Guid.Empty
        )
            return Result.Failure<FileSourceDeclaration>(FileErrors.InvalidSourceDeclaration);

        return Result.Success(new FileSourceDeclaration(service!, resourceType!, resourceId, role!, relatedCustomerId));
    }

    // Códigos en minúsculas con guion bajo, como los tipos de recurso de Work Profile.
    private static bool IsCode(string? value, int maxLength) =>
        !string.IsNullOrEmpty(value) && value.Length <= maxLength && CodePattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex CodePattern();
}
