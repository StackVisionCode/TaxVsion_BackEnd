using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;

namespace TaxVision.Postmaster.Tests.ErrorMapping;

/// <summary>
/// Fase 16.5 (hardening): antes de este fix, ninguno de los 4 códigos <c>*.NotFound</c> propios de
/// Postmaster (<c>SentMessage.NotFound</c> en <c>SentMessageRepository.cs</c>,
/// <c>TenantEmailProvider.NotFound</c> en <c>TenantEmailProviderRepository.cs</c>,
/// <c>SystemEmailProvider.NotFound</c> en <c>SystemEmailProviderRepository.cs</c>,
/// <c>SuppressionListEntry.NotFound</c> en <c>SuppressionListRepository.cs</c>/
/// <c>RemoveSuppressionEntryHandler.cs</c>) tenía entrada en <see cref="ErrorHttpMapping"/> — los 4
/// caían al <c>default</c> (400 Bad Request) en vez del 404 semánticamente correcto. Mismo patrón
/// exacto que el gap de <c>EventTemplateMapping.NotFound</c> ya cerrado en Scribe Fase 10.5. Barrido
/// exhaustivo de la Fase 16.5 confirmó que ningún otro código <c>*.NotFound</c> de Postmaster tiene el
/// mismo problema: <c>ProviderHealthStatus.NotFound</c> (<c>ProviderHealthStatusRepository.cs</c>) se
/// consume internamente en <c>GetProviderStatusHandler</c> vía <c>IsFailure</c>/<c>IsSuccess</c>, nunca
/// se propaga como <c>Result</c> failure hacia un controller; <c>SentMessage.RecipientNotFound</c>
/// (<c>SentMessage.RecordDeliveryEvent</c>) solo se invoca con IDs que ya vienen de
/// <c>message.Recipients</c> (ver <c>NotificationsEmailSendRequestedConsumer</c>/
/// <c>SendCorrespondenceMessageHandler</c>), inalcanzable en la práctica y con su <c>Result</c>
/// también descartado por ambos callers — ninguno de los dos llega nunca a un controller HTTP.
/// </summary>
public sealed class PostmasterNotFoundErrorHttpMappingTests
{
    [Fact]
    public void SentMessage_NotFound_maps_to_404()
    {
        var error = new Error("SentMessage.NotFound", "SentMessage was not found.");

        Assert.Equal(StatusCodes.Status404NotFound, error.ToHttpStatusCode());
    }

    [Fact]
    public void TenantEmailProvider_NotFound_maps_to_404()
    {
        var error = new Error("TenantEmailProvider.NotFound", "Provider not found for tenant.");

        Assert.Equal(StatusCodes.Status404NotFound, error.ToHttpStatusCode());
    }

    [Fact]
    public void SystemEmailProvider_NotFound_maps_to_404()
    {
        var error = new Error("SystemEmailProvider.NotFound", "Provider not found.");

        Assert.Equal(StatusCodes.Status404NotFound, error.ToHttpStatusCode());
    }

    [Fact]
    public void SuppressionListEntry_NotFound_maps_to_404()
    {
        var error = new Error("SuppressionListEntry.NotFound", "Address is not suppressed.");

        Assert.Equal(StatusCodes.Status404NotFound, error.ToHttpStatusCode());
    }

    [Fact]
    public void Unmapped_code_still_falls_through_to_400_default()
    {
        // Control: confirma que el "default" del switch sigue vivo y no se rompió al agregar las
        // 4 entradas nuevas — un código inventado que nunca va a existir debe seguir cayendo a 400.
        var error = new Error("SentMessage.SomeCodeThatDoesNotExist", "n/a");

        Assert.Equal(StatusCodes.Status400BadRequest, error.ToHttpStatusCode());
    }
}
