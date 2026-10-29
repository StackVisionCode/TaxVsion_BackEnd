using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ExternalParticipantErrorMappingTests
{
    [Fact]
    public void Conflicts_of_external_participants_map_to_409()
    {
        Assert.Equal(StatusCodes.Status409Conflict, FormParticipantErrors.EmailBelongsToCustomer.ToHttpStatusCode());
        Assert.Equal(StatusCodes.Status409Conflict, FormParticipantErrors.AlreadyConverted.ToHttpStatusCode());
        Assert.Equal(StatusCodes.Status409Conflict, PublicIntakeFormErrors.AlreadySubmitted.ToHttpStatusCode());
    }

    [Fact]
    public void A_missing_prospect_maps_to_404_and_an_invalid_reason_to_400()
    {
        Assert.Equal(StatusCodes.Status404NotFound, FormParticipantErrors.NotFound.ToHttpStatusCode());
        Assert.Equal(StatusCodes.Status400BadRequest, FormParticipantErrors.InvalidLostReason.ToHttpStatusCode());
    }

    [Fact]
    public void Becoming_a_client_reopens_a_lost_prospect()
    {
        var participant = FormActionTestDoubles.Participant();
        Assert.True(
            participant.MarkLost("No answer", FormActionTestDoubles.Responsible, FormActionTestDoubles.Now).IsSuccess
        );

        Assert.True(
            participant
                .LinkToCustomer(Guid.NewGuid(), FormActionTestDoubles.Responsible, FormActionTestDoubles.Now)
                .IsSuccess
        );

        Assert.False(participant.IsLost);
        Assert.Null(participant.LostReason);
    }
}
