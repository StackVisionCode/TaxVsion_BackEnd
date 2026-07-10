using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Authorization;
using TaxVision.Signature.Api.Common;
using TaxVision.Signature.Api.Requests;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Settings;
using TaxVision.Signature.Application.Settings.Commands.UpdateSettings;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

[ApiController]
[Route("signature/settings")]
public sealed class SettingsController(ITenantSignatureSettingsRepository repository, IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(SignaturePermissions.SettingsManage)]
    [ProducesResponseType(typeof(TenantSignatureSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var settings = await repository.GetByTenantIdAsync(tenantId, ct);
        if (settings is null)
            return NotFound();

        return Ok(TenantSignatureSettingsResponse.From(settings));
    }

    /// <summary>
    /// Reemplaza la configuración completa del tenant (semántica PUT). El tenant admin
    /// puede ajustar canales de verificación, expiración de tokens, recordatorios,
    /// generación de certificado, límites de documentos y política de retención.
    /// El TenantId se toma exclusivamente del JWT.
    /// </summary>
    [HttpPut]
    [HasPermission(SignaturePermissions.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromBody] UpdateSignatureSettingsBody body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        // Parse allowed channels bitmask from the string list.
        VerificationChannel allowedMask = VerificationChannel.None;
        foreach (var name in body.AllowedVerificationChannels)
        {
            if (
                !Enum.TryParse<VerificationChannel>(name, ignoreCase: true, out var ch)
                || ch == VerificationChannel.None
            )
                return BadRequest(
                    new Error("Signature.Settings.InvalidChannel", $"Unknown verification channel: '{name}'.")
                );
            allowedMask |= ch;
        }

        if (allowedMask == VerificationChannel.None)
            return BadRequest(
                new Error("Signature.Settings.NoChannels", "At least one verification channel must be specified.")
            );

        if (
            !Enum.TryParse<VerificationChannel>(
                body.DefaultVerificationChannel,
                ignoreCase: true,
                out var defaultChannel
            )
            || defaultChannel == VerificationChannel.None
        )
            return BadRequest(
                new Error(
                    "Signature.Settings.InvalidChannel",
                    $"Unknown verification channel: '{body.DefaultVerificationChannel}'."
                )
            );

        var cmd = new UpdateSignatureSettingsCommand(
            tenantId,
            userId,
            allowedMask,
            defaultChannel,
            body.DefaultTokenExpirationHours,
            body.RemindersEnabledByDefault,
            body.GenerateCertificateByDefault,
            body.DocumentLimits.MaxPdfBytes,
            body.DocumentLimits.MaxImageBytes,
            body.DocumentLimits.MaxPagesPerDocument,
            body.RetentionPolicy.RetentionYears,
            body.RetentionPolicy.AllowPurge
        );

        var result = await bus.InvokeAsync<Result>(cmd, ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
