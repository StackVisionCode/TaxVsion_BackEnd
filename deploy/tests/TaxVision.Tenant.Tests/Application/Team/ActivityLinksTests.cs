using TaxVision.Tenant.Application.Team;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Tests.Application.Team;

/// <summary>Rutas reales del CRM (TaxVsion_Front/src/app); el módulo dueño revalida al abrir.</summary>
public sealed class ActivityLinksTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(ActivityResourceTypes.Task, "/task?taskId=11111111-1111-1111-1111-111111111111")]
    [InlineData(
        ActivityResourceTypes.ClientRequest,
        "/clients/22222222-2222-2222-2222-222222222222?tab=work&requestId=11111111-1111-1111-1111-111111111111"
    )]
    [InlineData(ActivityResourceTypes.FormRequest, "/client-forms?formRequestId=11111111-1111-1111-1111-111111111111")]
    [InlineData(ActivityResourceTypes.Appointment, "/calendar?appointmentId=11111111-1111-1111-1111-111111111111")]
    [InlineData(ActivityResourceTypes.Note, "/notes?note=11111111-1111-1111-1111-111111111111")]
    [InlineData(ActivityResourceTypes.Customer, "/clients/11111111-1111-1111-1111-111111111111")]
    [InlineData(ActivityResourceTypes.SignatureRequest, "/signature/requests/11111111-1111-1111-1111-111111111111")]
    [InlineData(ActivityResourceTypes.Invoice, "/billing?invoiceId=11111111-1111-1111-1111-111111111111")]
    [InlineData(ActivityResourceTypes.SmsMessage, "/clients/22222222-2222-2222-2222-222222222222?tab=sms")]
    [InlineData(ActivityResourceTypes.EmailMessage, "/email?client=22222222-2222-2222-2222-222222222222")]
    [InlineData(ActivityResourceTypes.EmailThread, "/email?threadId=11111111-1111-1111-1111-111111111111")]
    [InlineData(ActivityResourceTypes.Document, "/clients/22222222-2222-2222-2222-222222222222?tab=documents")]
    [InlineData(ActivityResourceTypes.SupportTicket, "/support")]
    public void Each_resource_type_links_to_its_crm_screen(string resourceType, string expected)
    {
        Assert.Equal(expected, ActivityLinks.For(resourceType, Id, CustomerId));
    }

    [Theory]
    [InlineData(ActivityResourceTypes.ClientRequest)]
    [InlineData(ActivityResourceTypes.SmsMessage)]
    [InlineData(ActivityResourceTypes.Document)]
    public void A_customer_screen_link_without_a_customer_has_no_link(string resourceType)
    {
        Assert.Null(ActivityLinks.For(resourceType, Id, null));
    }

    // Correo nuevo a alguien que no es cliente: abre la vista "Not a client".
    [Fact]
    public void A_new_email_without_a_customer_opens_not_a_client_mail()
    {
        Assert.Equal("/email?nc=1", ActivityLinks.For(ActivityResourceTypes.EmailMessage, Id, null));
    }

    [Fact]
    public void Every_resource_type_of_the_contract_has_a_link()
    {
        foreach (var verb in ActivityVerbs.All)
            Assert.NotNull(ActivityLinks.For(ActivityVerbs.ResourceTypeOf(verb)!, Id, CustomerId));
    }
}
