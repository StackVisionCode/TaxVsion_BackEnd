using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Abstractions;

public interface IJwtTokenGenerator
{
    string Generate(User user, string effectiveTimeZoneId);
}
