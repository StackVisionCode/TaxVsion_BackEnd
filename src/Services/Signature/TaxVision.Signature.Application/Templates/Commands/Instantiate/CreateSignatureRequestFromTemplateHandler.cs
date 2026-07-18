using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Templates;
using Wolverine;

namespace TaxVision.Signature.Application.Templates.Commands.Instantiate;

/// <summary>
/// Instancia una plantilla publicada. Fases explícitas por método privado:
/// <list type="number">
///   <item>Cargar plantilla y validar estado <c>Published</c>.</item>
///   <item>Validar bindings vs slots (1-1, sin sobrantes ni faltantes).</item>
///   <item>Construir value objects de todos los signers (fail-fast).</item>
///   <item>Promover a Ready si el archivo ya está disponible (misma lógica de <see cref="Requests.Commands.Create.CreateSignatureRequestHandler"/>).</item>
///   <item>Crear el aggregate <c>SignatureRequest</c>, agregar signers y campos.</item>
///   <item>Persistir y publicar <c>SignatureRequestCreated</c>.</item>
/// </list>
/// </summary>
public static class CreateSignatureRequestFromTemplateHandler
{
    public static async Task<Result<SignatureRequestResponse>> Handle(
        CreateSignatureRequestFromTemplateCommand cmd,
        ISignatureTemplateRepository templateRepository,
        ISignatureRequestRepository requestRepository,
        ICustomerEmailProjectionRepository customerProjectionRepository,
        IFileMetadataRefRepository fileRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var template = await templateRepository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Failure("Signature.Template.NotFound", "The signature template does not exist for this tenant.");
        if (template.Status != SignatureTemplateStatus.Published)
            return Failure("Signature.Template.NotPublished", "Only published templates can be instantiated.");

        var bindingValidation = ValidateBindings(cmd.SlotBindings, template);
        if (bindingValidation.IsFailure)
            return Result.Failure<SignatureRequestResponse>(bindingValidation.Error);

        var signerVOs = await BuildSignerValueObjectsAsync(cmd, template, customerProjectionRepository, ct);
        if (signerVOs.IsFailure)
            return Result.Failure<SignatureRequestResponse>(signerVOs.Error);

        var requestResult = CreateDraft(cmd, template);
        if (requestResult.IsFailure)
            return Result.Failure<SignatureRequestResponse>(requestResult.Error);

        var request = requestResult.Value;
        var populated = PopulateSignersAndFields(request, template, signerVOs.Value);
        if (populated.IsFailure)
            return Result.Failure<SignatureRequestResponse>(populated.Error);

        await TryPromoteToReadyIfFileAvailable(request, cmd, fileRepository, ct);
        await requestRepository.AddAsync(request, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await PublishCreatedEventAsync(request, template, correlation, bus);

        return Result.Success(SignatureRequestResponse.From(request));
    }

    private sealed record SignerValueObjects(
        int SlotOrder,
        SignerEmail Email,
        SignerFullName FullName,
        Guid? MappedCustomerId
    );

    // ============== Fase 2: validar bindings ==============

    private static Result ValidateBindings(IReadOnlyList<SlotBinding> bindings, SignatureTemplate template)
    {
        if (bindings.Count != template.Slots.Count)
            return Result.Failure(
                new Error(
                    "Signature.Template.BindingMismatch",
                    $"Expected {template.Slots.Count} bindings but received {bindings.Count}."
                )
            );

        var slotOrders = template.Slots.Select(s => s.Order).ToHashSet();
        var seen = new HashSet<int>(bindings.Count);
        foreach (var binding in bindings)
        {
            if (!slotOrders.Contains(binding.SlotOrder))
                return Result.Failure(
                    new Error(
                        "Signature.Template.BindingUnknownSlot",
                        $"Binding references unknown slot order {binding.SlotOrder}."
                    )
                );
            if (!seen.Add(binding.SlotOrder))
                return Result.Failure(
                    new Error(
                        "Signature.Template.BindingDuplicate",
                        $"Binding duplicates slot order {binding.SlotOrder}."
                    )
                );
        }
        return Result.Success();
    }

    // ============== Fase 3: construir VOs por slot ==============

    private static async Task<Result<IReadOnlyList<SignerValueObjects>>> BuildSignerValueObjectsAsync(
        CreateSignatureRequestFromTemplateCommand cmd,
        SignatureTemplate template,
        ICustomerEmailProjectionRepository customerProjectionRepository,
        CancellationToken ct
    )
    {
        var bindingsBySlot = cmd.SlotBindings.ToDictionary(b => b.SlotOrder);
        var signers = new List<SignerValueObjects>(template.Slots.Count);
        foreach (var slot in template.Slots.OrderBy(s => s.Order))
        {
            var binding = bindingsBySlot[slot.Order];
            var emailResult = SignerEmail.Create(binding.Email);
            if (emailResult.IsFailure)
                return Result.Failure<IReadOnlyList<SignerValueObjects>>(emailResult.Error);
            var nameResult = SignerFullName.Create(binding.FullName);
            if (nameResult.IsFailure)
                return Result.Failure<IReadOnlyList<SignerValueObjects>>(nameResult.Error);

            var mapped = await FindMappedCustomerAsync(
                cmd.TenantId,
                emailResult.Value,
                customerProjectionRepository,
                ct
            );
            signers.Add(new SignerValueObjects(slot.Order, emailResult.Value, nameResult.Value, mapped));
        }
        return Result.Success<IReadOnlyList<SignerValueObjects>>(signers);
    }

    private static async Task<Guid?> FindMappedCustomerAsync(
        Guid tenantId,
        SignerEmail email,
        ICustomerEmailProjectionRepository repo,
        CancellationToken ct
    )
    {
        var match = await repo.FindActiveByEmailAsync(tenantId, email.Value, ct);
        return match?.CustomerId;
    }

    // ============== Fase 4: factory del aggregate ==============

    private static Result<SignatureRequest> CreateDraft(
        CreateSignatureRequestFromTemplateCommand cmd,
        SignatureTemplate template
    ) =>
        SignatureRequest.CreateDraft(
            tenantId: cmd.TenantId,
            createdByUserId: cmd.CreatedByUserId,
            title: template.Title,
            description: cmd.DescriptionOverride ?? template.Description,
            category: template.Category,
            originalFileId: cmd.OriginalFileId,
            tokenExpirationHours: template.DefaultTokenExpirationHours,
            requiresSequentialSigning: template.RequiresSequentialSigning,
            requiresConsent: template.RequiresConsent,
            generateCertificate: template.GenerateCertificate
        );

    // ============== Fase 5: agregar signers y campos ==============

    private static Result PopulateSignersAndFields(
        SignatureRequest request,
        SignatureTemplate template,
        IReadOnlyList<SignerValueObjects> signers
    )
    {
        var signerIdBySlotOrder = new Dictionary<int, Guid>(signers.Count);
        foreach (var signer in signers)
        {
            var addResult = request.AddSigner(signer.Email, signer.FullName, signer.MappedCustomerId);
            if (addResult.IsFailure)
                return Result.Failure(addResult.Error);
            signerIdBySlotOrder[signer.SlotOrder] = addResult.Value.Id;
        }

        foreach (var field in template.Fields)
        {
            var signerId = signerIdBySlotOrder[field.SlotOrder];
            var placeResult = request.PlaceField(signerId, field.Kind, field.Position, field.Label, field.IsRequired);
            if (placeResult.IsFailure)
                return Result.Failure(placeResult.Error);
        }
        return Result.Success();
    }

    // ============== Fase 6: promoción a Ready + publicar ==============
    // Reutiliza la misma lógica que la creación directa: si el archivo ya está
    // disponible se promueve a Ready; caso contrario espera el FileAvailable consumer.

    private static async Task TryPromoteToReadyIfFileAvailable(
        SignatureRequest request,
        CreateSignatureRequestFromTemplateCommand cmd,
        IFileMetadataRefRepository fileRepository,
        CancellationToken ct
    )
    {
        var file = await fileRepository.GetByFileIdAsync(cmd.TenantId, cmd.OriginalFileId, ct);
        if (file is null || file.Status != FileScanStatus.Available)
            return;
        if (string.IsNullOrEmpty(file.ChecksumSha256))
            return;

        var hashResult = DocumentHash.Create(file.ChecksumSha256);
        if (hashResult.IsFailure)
            return;

        request.MarkReadyForSending(hashResult.Value);
    }

    private static Task PublishCreatedEventAsync(
        SignatureRequest request,
        SignatureTemplate template,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestCreatedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    Title = request.Title,
                    Category = request.Category.ToString(),
                    OriginalFileId = request.OriginalFileId,
                    TokenExpirationHours = request.TokenExpirationHours,
                    RequiresSequentialSigning = request.RequiresSequentialSigning,
                    SignerCount = request.Signers.Count,
                    ExpiresAtUtc = request.ExpiresAtUtc,
                }
            )
            .AsTask();

    // ============== Helpers ==============

    private static Result<SignatureRequestResponse> Failure(string code, string message) =>
        Result.Failure<SignatureRequestResponse>(new Error(code, message));
}
