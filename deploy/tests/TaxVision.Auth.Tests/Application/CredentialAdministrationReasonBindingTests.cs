using System.Text.Json;
using System.Text.Json.Serialization;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Application.Customers;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Un <c>reasonCode</c> desconocido no rompe el binding: llega al dominio y responde
/// <c>Auth.CredentialAdmin.InvalidReason</c> (staff, portal y MFA del portal).
/// </summary>
public sealed class CredentialAdministrationReasonBindingTests
{
    // Mismas opciones que Auth.Api registra en MVC.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void An_unknown_staff_reason_reaches_the_domain_as_invalid()
    {
        var request = JsonSerializer.Deserialize<CredentialAdministrationRequest>(
            """{"reasonCode":"NotAReason"}""",
            Options
        )!;

        AssertInvalid(request.ReasonCode);
    }

    [Fact]
    public void An_unknown_portal_reason_reaches_the_domain_as_invalid()
    {
        var envelope = JsonSerializer.Deserialize<PortalCredentialCommandEnvelope>(
            $$"""{"requestedByUserId":"{{Guid.NewGuid()}}","reauthenticatedAtUtc":"2026-11-27T10:00:00Z","reasonCode":"NotAReason"}""",
            Options
        )!;

        Assert.NotNull(envelope.ReasonCode);
        AssertInvalid(envelope.ReasonCode!.Value);
    }

    [Fact]
    public void An_unknown_portal_mfa_reason_reaches_the_domain_as_invalid()
    {
        var request = JsonSerializer.Deserialize<InternalCustomerPortalMfaController.ResetPortalMfaRequest>(
            $$"""{"requestedByUserId":"{{Guid.NewGuid()}}","reasonCode":"NotAReason"}""",
            Options
        )!;

        Assert.NotNull(request.ReasonCode);
        AssertInvalid(request.ReasonCode!.Value);
    }

    [Theory]
    [InlineData("\"SharedPassword\"", CredentialAdministrationReasonCode.SharedPassword)]
    [InlineData("\"officePolicy\"", CredentialAdministrationReasonCode.OfficePolicy)]
    [InlineData("3", CredentialAdministrationReasonCode.SuspectedCompromise)]
    public void A_known_reason_keeps_binding(string json, CredentialAdministrationReasonCode expected)
    {
        var request = JsonSerializer.Deserialize<CredentialAdministrationRequest>(
            $$"""{"reasonCode":{{json}}}""",
            Options
        )!;
        var envelope = JsonSerializer.Deserialize<PortalCredentialCommandEnvelope>(
            $$"""{"requestedByUserId":"{{Guid.NewGuid()}}","reasonCode":{{json}}}""",
            Options
        )!;

        Assert.Equal(expected, request.ReasonCode);
        Assert.Equal(expected, envelope.ReasonCode);
    }

    [Fact]
    public void A_missing_portal_reason_stays_null()
    {
        var envelope = JsonSerializer.Deserialize<PortalCredentialCommandEnvelope>(
            $$"""{"requestedByUserId":"{{Guid.NewGuid()}}","reasonCode":null}""",
            Options
        )!;

        Assert.Null(envelope.ReasonCode);
    }

    private static void AssertInvalid(CredentialAdministrationReasonCode code)
    {
        var reason = CredentialAdministrationReason.Create(code, null);
        Assert.True(reason.IsFailure);
        Assert.Equal(CredentialAdministrationErrors.InvalidReason.Code, reason.Error.Code);
    }
}
