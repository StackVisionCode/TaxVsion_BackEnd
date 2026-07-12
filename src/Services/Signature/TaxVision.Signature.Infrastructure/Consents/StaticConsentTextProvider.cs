using System.Security.Cryptography;
using System.Text;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Consents;

/// <summary>
/// Provider por defecto de textos de consent. Devuelve un texto estático por par
/// (Category, Language) con versión slug. Cuando el negocio necesite un editor CMS
/// por tenant, se sustituye la implementación sin tocar el flujo de firma. El slug
/// de versión sólo cambia cuando el texto cambia — permite ver en el audit qué
/// versión aceptó cada firmante.
/// </summary>
public sealed class StaticConsentTextProvider : IConsentTextProvider
{
    public ConsentTextSnapshot Resolve(SignatureCategory category, string language)
    {
        var normalizedLanguage = NormalizeLanguage(language);
        var (version, text) = ResolveEntry(category, normalizedLanguage);
        var hash = ComputeSha256(text);
        return new ConsentTextSnapshot(version, normalizedLanguage, text, hash);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada categoría con su propia función factoría
    // ------------------------------------------------------------------

    private static (string Version, string Text) ResolveEntry(SignatureCategory category, string language) =>
        category switch
        {
            SignatureCategory.ConsentToDisclose when language == "Es" => (
                "consent.disclose.v1.es",
                "Autorizo a mi preparador de impuestos a divulgar la información de mi declaración federal en los términos de la Sección 7216 del Código de Rentas Internas de los Estados Unidos."
            ),
            SignatureCategory.ConsentToDisclose => (
                "consent.disclose.v1.en",
                "I authorize my tax preparer to disclose information from my federal tax return under Section 7216 of the U.S. Internal Revenue Code."
            ),
            SignatureCategory.BankAuth when language == "Es" => (
                "consent.bankauth.v1.es",
                "Autorizo el depósito directo del reembolso federal en la cuenta bancaria proporcionada."
            ),
            SignatureCategory.BankAuth => (
                "consent.bankauth.v1.en",
                "I authorize the direct deposit of my federal refund into the bank account provided."
            ),
            SignatureCategory.EngagementLetter when language == "Es" => (
                "consent.engagement.v1.es",
                "Acepto los términos del servicio de preparación de impuestos y las tarifas descritas en este documento."
            ),
            SignatureCategory.EngagementLetter => (
                "consent.engagement.v1.en",
                "I accept the terms of tax preparation service and the fees described in this document."
            ),
            SignatureCategory.Fiscal when language == "Es" => (
                "consent.fiscal.v1.es",
                "Confirmo que la información fiscal contenida en este documento es correcta y completa a mi mejor conocimiento."
            ),
            _ when language == "Es" => (
                "consent.generic.v1.es",
                "Acepto los términos descritos en este documento y confirmo mi intención de firmarlo electrónicamente."
            ),
            SignatureCategory.Fiscal => (
                "consent.fiscal.v1.en",
                "I confirm that the tax information contained in this document is correct and complete to the best of my knowledge."
            ),
            _ => (
                "consent.generic.v1.en",
                "I accept the terms described in this document and confirm my intent to sign it electronically."
            ),
        };

    private static string NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "En";
        var lower = language.Trim().ToLowerInvariant();
        return lower switch
        {
            "es" => "Es",
            "en" => "En",
            _ => "En",
        };
    }

    private static string ComputeSha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
