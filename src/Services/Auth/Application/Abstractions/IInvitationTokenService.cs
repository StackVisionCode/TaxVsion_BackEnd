namespace TaxVision.Auth.Application.Abstractions;

public sealed record InvitationToken(string RawToken, string TokenHash);

public interface IInvitationTokenService
{
    InvitationToken Generate();
    string Hash(string rawToken);
}
