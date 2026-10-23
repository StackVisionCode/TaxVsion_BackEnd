using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormTemplateErrorMappingTests
{
    [Theory]
    [InlineData("FormTemplate.NotFound")]
    [InlineData("FormTemplateVersion.NotFound")]
    [InlineData("FormRequest.NotFound")]
    [InlineData("FormAttachment.NotFound")]
    [InlineData("FormSensitiveAnswer.NotFound")]
    [InlineData("FormLibrary.NotFound")]
    [InlineData("FormRequest.AttachmentNotFound")]
    public void Missing_form_resources_map_to_404(string code) =>
        Assert.Equal(StatusCodes.Status404NotFound, new Error(code, "missing").ToHttpStatusCode());

    [Theory]
    [InlineData("FormTemplate.Archived")]
    [InlineData("FormTemplate.NotArchived")]
    [InlineData("FormTemplate.NothingToPublish")]
    [InlineData("FormTemplate.HasPublishedVersions")]
    [InlineData("FormTemplate.ConcurrencyConflict")]
    [InlineData("FormRequest.InvalidTransition")]
    [InlineData("FormRequest.NotEditable")]
    [InlineData("FormRequest.AlreadyOpenForTemplate")]
    [InlineData("FormRequest.AttachmentsNotReady")]
    [InlineData("FormRequest.ReminderLimitReached")]
    [InlineData("FormRequest.ConcurrencyConflict")]
    [InlineData("FormRequest.ProfileUpdateAlreadyApproved")]
    [InlineData("FormRequest.CustomerUnavailable")]
    [InlineData("FormRequest.TemplateNotPublished")]
    [InlineData("FormRequest.AttachmentAlreadyRegistered")]
    public void Form_state_conflicts_map_to_409(string code) =>
        Assert.Equal(StatusCodes.Status409Conflict, new Error(code, "conflict").ToHttpStatusCode());

    [Theory]
    [InlineData("FormRequest.TextContainsSensitiveData")]
    [InlineData("FormRequest.PrefillNotAvailable")]
    public void Invalid_staff_input_maps_to_400(string code) =>
        Assert.Equal(StatusCodes.Status400BadRequest, new Error(code, "invalid").ToHttpStatusCode());
}
