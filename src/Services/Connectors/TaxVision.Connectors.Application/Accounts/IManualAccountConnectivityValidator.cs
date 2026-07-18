using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Prueba real de conectividad contra un servidor IMAP/SMTP provisto a mano por el usuario, antes de
/// persistir la cuenta — a diferencia de OAuth (donde el intercambio del authorization code ya valida
/// el grant), acá no hay nada que garantice que el host/usuario/contraseña son correctos hasta que se
/// intenta conectar. Sin esto, una cuenta manual mal tipeada quedaría "Active" pero rota.
/// </summary>
public interface IManualAccountConnectivityValidator
{
    Task<Result> ValidateImapAsync(
        string host,
        int port,
        bool useSsl,
        string username,
        string password,
        CancellationToken ct = default
    );

    Task<Result> ValidateSmtpAsync(
        string host,
        int port,
        bool useStartTls,
        string username,
        string password,
        CancellationToken ct = default
    );
}
