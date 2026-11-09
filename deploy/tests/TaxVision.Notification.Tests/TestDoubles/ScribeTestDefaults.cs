using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Respuesta de los dobles de Scribe que no renderizan texto plano.</summary>
internal static class ScribeTestDefaults
{
    public static Task<Result<ScribeRenderedText>> TextUnsupported() =>
        Task.FromResult(
            Result.Failure<ScribeRenderedText>(
                new Error("Notification.ScribeTextUnsupported", "Text rendering is not supported by this client.")
            )
        );
}
