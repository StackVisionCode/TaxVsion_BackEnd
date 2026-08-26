using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Results;

/// <summary>
/// BB-08 — prioridad 1: este mapping decide el status de ~342 call sites y no tenía ningún test.
/// Además de fijar un representante por grupo semántico, la fitness function del final cierra el
/// gap que el propio archivo documenta **tres veces** (Scribe 10.5, Postmaster 16.5, Notes 7): un
/// código <c>*.NotFound</c> nuevo que nadie agrega a la lista y cae al default 400.
/// </summary>
public sealed class ErrorHttpMappingTests
{
    private static int Map(string code) => new Error(code, "irrelevante").ToHttpStatusCode();

    [Theory]
    [InlineData("User.NotFound", StatusCodes.Status404NotFound)]
    [InlineData("Auth.Invalid", StatusCodes.Status401Unauthorized)]
    [InlineData("Auth.HandoffInvalid", StatusCodes.Status401Unauthorized)]
    [InlineData("File.Forbidden", StatusCodes.Status403Forbidden)]
    [InlineData("Role.NameConflict", StatusCodes.Status409Conflict)]
    [InlineData("Codes.CodeQuote.Expired", StatusCodes.Status410Gone)]
    [InlineData("File.ZipTooLarge", StatusCodes.Status413PayloadTooLarge)]
    [InlineData("Codes.CodeDefinition.NotActive", StatusCodes.Status422UnprocessableEntity)]
    [InlineData("Auth.LockedOut", StatusCodes.Status429TooManyRequests)]
    [InlineData("ConnectorsClient.UnexpectedStatus", StatusCodes.Status502BadGateway)]
    [InlineData("ConnectorsClient.Unavailable", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("SendMessageHandler.Timeout", StatusCodes.Status504GatewayTimeout)]
    public void CadaGrupoSemantico_MapeaASuStatus(string code, int expected) => Assert.Equal(expected, Map(code));

    [Theory]
    [InlineData("Codigo.Que.Nadie.Mapeo")]
    [InlineData("")]
    public void UnCodigoDesconocido_CaeA400(string code) => Assert.Equal(StatusCodes.Status400BadRequest, Map(code));

    [Fact]
    public void ErrorNone_CaeA400_YNoRevienta() =>
        Assert.Equal(StatusCodes.Status400BadRequest, Error.None.ToHttpStatusCode());

    // ------------------------------------------------------- los arms por patrón (Growth/Codes)

    [Theory]
    [InlineData("Codes.CodeDefinition.NotFound", StatusCodes.Status404NotFound)]
    [InlineData("ReferralProgram.NotFound", StatusCodes.Status404NotFound)]
    [InlineData("Codes.CodeQuote.InvalidTransition", StatusCodes.Status409Conflict)]
    [InlineData("ReferralAward.LimitReached", StatusCodes.Status409Conflict)]
    [InlineData("Codes.PaymentOutcomeVerifier.Unreachable", StatusCodes.Status503ServiceUnavailable)]
    public void LosArmsPorPrefijoYSufijo_CubrenLosCodigosDeGrowth(string code, int expected) =>
        Assert.Equal(expected, Map(code));

    [Fact]
    public void UnSufijoConocidoSinElPrefijoDeGrowth_NoEntraPorElArmDePatron() =>
        // "Cualquiera.LimitReached" no es Codes.*/Referral*, así que cae al default. Fija que el
        // arm está acotado por prefijo a propósito y no captura medio catálogo.
        Assert.Equal(StatusCodes.Status400BadRequest, Map("Cualquiera.LimitReached"));

    // ------------------------------------------------------- fitness function

    /// <summary>
    /// Códigos <c>*.NotFound</c> que a propósito NO son 404. Agregar una entrada acá es una decisión
    /// de diseño y necesita justificación escrita — no es el lugar para tapar un olvido.
    /// </summary>
    private static readonly Dictionary<string, string> NotFoundExceptions = new()
    {
        ["SendCorrespondenceMessageHandler.AccountNotFound"] =
            "403 a propósito: es un endpoint M2M y un 404 revelaría si la cuenta existe en otro tenant.",
    };

    [Fact]
    public void TodoCodigoNotFoundDeclaradoEnElMapping_DevuelveUn404()
    {
        // El archivo ya arrastra tres incidentes del mismo tipo: alguien agrega un
        // "Algo.NotFound" en un servicio, no lo suma acá, y el endpoint responde 400. Esto lo
        // convierte en imposible para todos los códigos que el mapping sí declara.
        var source = File.ReadAllText(LocateMappingSource());
        var declared = Regex
            .Matches(source, @"""([A-Za-z0-9_.]+(?:NotFound))""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(declared);

        var misrouted = declared
            .Where(code => !NotFoundExceptions.ContainsKey(code))
            .Where(code => Map(code) != StatusCodes.Status404NotFound)
            .ToList();

        Assert.True(
            misrouted.Count == 0,
            $"Estos códigos *.NotFound no devuelven 404: {string.Join(", ", misrouted)}. "
                + "Si es a propósito, decláralo en NotFoundExceptions con el motivo."
        );

        // Las excepciones declaradas tienen que seguir existiendo: si alguien renombra el código,
        // la entrada queda huérfana y la regla vuelve a aplicarse sin que nadie se entere.
        Assert.All(NotFoundExceptions.Keys, code => Assert.Contains(code, declared));
    }

    /// <summary>Sube desde el binario hasta la raíz del repo para leer el .cs del mapping.</summary>
    private static string LocateMappingSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(
            dir!.FullName,
            "src",
            "BuildingBlocks",
            "BuildingBlocks.Web",
            "Results",
            "ErrorHttpMapping.cs"
        );

        Assert.True(File.Exists(path), $"No se encontró {path}");
        return path;
    }
}
