namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

public interface IFormIntakeBotProtection
{
    bool IsReady { get; }
    Task<bool> VerifyAsync(string token, string clientIp, CancellationToken ct = default);
}
