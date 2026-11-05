namespace TaxVision.Sms.Infrastructure.Providers;

/// <summary>Timeout de la petición de envío. Polly no lo reintenta: el POST pudo llegar al proveedor.</summary>
public sealed class SmsSendTimeoutException(Exception innerException)
    : Exception("The SMS provider did not answer the send request in time.", innerException);

/// <summary>POST de envío a un proveedor dentro del pipeline de resiliencia.</summary>
public static class SmsSendHttp
{
    // Un timeout sale como SmsSendTimeoutException: el pipeline solo reintenta HttpRequestException/TaskCanceledException.
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex)
        {
            throw new SmsSendTimeoutException(ex);
        }
    }

    // Timeout o corte de transporte con la petición quizá ya recibida. Sin conexión (DNS, conexión, TLS, proxy) no salió.
    public static bool IsOutcomeUnknown(Exception ex) =>
        ex switch
        {
            SmsSendTimeoutException or TaskCanceledException => true,
            HttpRequestException http => http.HttpRequestError
                is not (
                    HttpRequestError.NameResolutionError
                    or HttpRequestError.ConnectionError
                    or HttpRequestError.SecureConnectionError
                    or HttpRequestError.ProxyTunnelError
                ),
            _ => false,
        };
}
