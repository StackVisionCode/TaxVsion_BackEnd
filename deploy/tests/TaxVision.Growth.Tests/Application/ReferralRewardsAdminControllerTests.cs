using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Growth.Api.Controllers;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Referrals.Application.Rewards;
using TaxVision.Referrals.Application.Rewards.GetReferralRewardNotice;
using TaxVision.Referrals.Application.Rewards.ReviewRewardCase;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Application;

// El aviso del rechazo sale del handler, en la misma unidad que el cambio de estado; el controller ya no publica.
public sealed class ReferralRewardsAdminControllerTests
{
    [Fact]
    public async Task Rejecting_does_not_publish_from_the_controller()
    {
        var tenantId = Guid.NewGuid();
        var rewardCaseId = Guid.NewGuid();
        var bus = new ScriptedBus(message =>
            message switch
            {
                ReviewRewardCaseCommand => Result.Success(
                    new ReviewRewardCaseResult(rewardCaseId, ReferralRewardCaseStatus.Cancelled)
                ),
                GetReferralRewardNoticeQuery => Result.Success<ReferralRewardNotice?>(
                    new ReferralRewardNotice(
                        tenantId,
                        rewardCaseId,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        2500,
                        "USD",
                        DateTime.UtcNow
                    )
                ),
                _ => throw new InvalidOperationException(message.GetType().Name),
            }
        );
        var controller = new ReferralRewardsAdminController(bus, new CorrelationContext())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = PlatformAdmin() },
            },
        };

        var response = await controller.Reject(
            rewardCaseId,
            new ReferralRewardsAdminController.ReviewRequest(tenantId, "self referral"),
            default
        );

        Assert.IsType<OkObjectResult>(response);
        Assert.Empty(bus.Published);
    }

    private static ClaimsPrincipal PlatformAdmin() =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimNames.TenantId, PlatformTenant.Id.ToString()),
                    new Claim("sub", Guid.NewGuid().ToString()),
                ],
                "test"
            )
        );
}
