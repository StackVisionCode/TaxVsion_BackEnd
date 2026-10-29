using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Infrastructure.CloudStorage;

/// <summary>
/// Reasignación autenticada FormParticipant → Customer con el token M2M de tasks-worker. CloudStorage
/// compara el dueño actual archivo por archivo; acá solo se traduce la respuesta y se mide.
/// </summary>
internal sealed class FormFileReassignmentClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    IClientFormsMetrics metrics,
    ILogger<FormFileReassignmentClient> logger
) : IFormFileReassignmentClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Error Unavailable = new(
        "FormRequest.FileReassignmentUnavailable",
        "The file reassignment service is unavailable."
    );

    public async Task<Result<IReadOnlyList<FormFileReassignmentResult>>> ReassignAsync(
        FormFileReassignmentRequest request,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(request.TenantId, ct);
        if (string.IsNullOrWhiteSpace(token))
            return Failure(request, "no_token");
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "internal/files/reassign-form-participant")
            {
                Content = JsonContent.Create(request, options: Json),
            };
            message.Headers.Authorization = new("Bearer", token);
            using var response = await httpClient.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                return Failure(request, $"http_{(int)response.StatusCode}");
            var body = await response.Content.ReadFromJsonAsync<ReassignmentDto>(Json, ct);
            return body?.Files is null ? Failure(request, "invalid_response") : Success(body.Files);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Could not reassign files of form request {FormRequestId}.", request.FormRequestId);
            return Failure(request, "unreachable");
        }
    }

    private Result<IReadOnlyList<FormFileReassignmentResult>> Success(IReadOnlyList<FileDto> files)
    {
        var results = new List<FormFileReassignmentResult>(files.Count);
        foreach (var file in files)
        {
            // Un estado desconocido no se registra como hecho: se trata como si el archivo no existiera.
            var outcome = FormFileReassignmentOutcomes.IsKnown(file.Status)
                ? file.Status!
                : FormFileReassignmentOutcomes.NotFound;
            metrics.RecordFileReassignment(outcome);
            results.Add(new FormFileReassignmentResult(file.FileId, outcome));
        }
        return Result.Success<IReadOnlyList<FormFileReassignmentResult>>(results);
    }

    // La métrica agrupa toda caída como error; el motivo concreto queda en el log.
    private Result<IReadOnlyList<FormFileReassignmentResult>> Failure(
        FormFileReassignmentRequest request,
        string reason
    )
    {
        if (reason != "unreachable")
            logger.LogWarning(
                "Could not reassign files of form request {FormRequestId}: {Reason}.",
                request.FormRequestId,
                reason
            );
        metrics.RecordFileReassignment(ClientFormsMetricValues.Error);
        return Result.Failure<IReadOnlyList<FormFileReassignmentResult>>(Unavailable);
    }

    private sealed record ReassignmentDto(IReadOnlyList<FileDto>? Files);

    private sealed record FileDto(Guid FileId, string? Status);
}
