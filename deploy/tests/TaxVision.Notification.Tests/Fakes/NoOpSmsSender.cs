using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests;

/// <summary>ISmsSender de prueba: no envía nada y devuelve éxito. Para tests que ejercitan la rama email.</summary>
internal sealed class NoOpSmsSender : ISmsSender
{
    public int Sent { get; private set; }
    public string? LastPhone { get; private set; }
    public string? LastText { get; private set; }

    public Task<Result> SendAsync(string phoneNumber, string text, CancellationToken ct = default)
    {
        Sent++;
        LastPhone = phoneNumber;
        LastText = text;
        return Task.FromResult(Result.Success());
    }
}
