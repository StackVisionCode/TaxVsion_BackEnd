using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Resultado de UNA prueba (IMAP o SMTP): bien, o el código y mensaje del fallo.</summary>
public sealed record ConnectionCheck(bool Ok, string? ErrorCode, string? Message)
{
    public static ConnectionCheck From(Result result) =>
        result.IsSuccess ? new(true, null, null) : new(false, result.Error.Code, result.Error.Message);
}

/// <summary>Las dos pruebas del alta manual, siempre ambas: el usuario ve cuál falló.</summary>
public sealed record TestManualConnectionResult(ConnectionCheck Imap, ConnectionCheck Smtp);
