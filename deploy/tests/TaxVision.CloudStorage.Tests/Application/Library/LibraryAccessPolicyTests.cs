using TaxVision.CloudStorage.Application.Library;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class LibraryAccessPolicyTests
{
    private static readonly LibrarySource[] AllSources =
    [
        LibrarySource.Documents,
        LibrarySource.Signature,
        LibrarySource.Billing,
        LibrarySource.Financial,
    ];

    [Theory]
    [InlineData(LibrarySource.Documents)]
    [InlineData(LibrarySource.Signature)]
    [InlineData(LibrarySource.Billing)]
    public void With_the_flag_off_only_financial_is_restricted(LibrarySource source)
    {
        var access = Access(viewAll: false, explorerRestrict: false);

        Assert.False(LibraryAccessPolicy.IsRestricted(access, source));
        Assert.True(LibraryAccessPolicy.IsRestricted(access, LibrarySource.Financial));
    }

    [Theory]
    [InlineData(LibrarySource.Documents)]
    [InlineData(LibrarySource.Signature)]
    [InlineData(LibrarySource.Billing)]
    [InlineData(LibrarySource.Financial)]
    public void With_the_flag_on_every_source_is_restricted(LibrarySource source)
    {
        Assert.True(LibraryAccessPolicy.IsRestricted(Access(viewAll: false, explorerRestrict: true), source));
    }

    [Theory]
    [InlineData(LibrarySource.Documents)]
    [InlineData(LibrarySource.Financial)]
    public void View_all_lifts_every_restriction(LibrarySource source)
    {
        Assert.False(LibraryAccessPolicy.IsRestricted(Access(viewAll: true, explorerRestrict: true), source));
    }

    [Fact]
    public void Without_a_filter_every_readable_source_is_requested_in_catalog_order()
    {
        var access = Access(readable: [LibrarySource.Billing, LibrarySource.Documents]);

        Assert.Equal([LibrarySource.Documents, LibrarySource.Billing], LibraryAccessPolicy.Requested(access, []));
    }

    [Fact]
    public void A_filter_is_intersected_with_the_readable_sources()
    {
        var access = Access(readable: [LibrarySource.Documents, LibrarySource.Signature]);

        var requested = LibraryAccessPolicy.Requested(access, [LibrarySource.Signature, LibrarySource.Billing]);

        Assert.Equal([LibrarySource.Signature], requested);
    }

    [Fact]
    public void A_filter_with_only_unreadable_sources_requests_nothing()
    {
        var access = Access(readable: [LibrarySource.Documents]);

        Assert.Empty(LibraryAccessPolicy.Requested(access, [LibrarySource.Billing]));
    }

    [Fact]
    public void The_read_scope_carries_each_restriction_and_the_assigned_clients()
    {
        var assigned = Guid.NewGuid();
        var access = Access(viewAll: false, explorerRestrict: false, assigned: [assigned]);

        var scope = LibraryAccessPolicy.ScopeFor(access, [LibrarySource.Documents, LibrarySource.Financial]);

        Assert.Equal(
            [
                new LibrarySourceScope(LibrarySource.Documents, false),
                new LibrarySourceScope(LibrarySource.Financial, true),
            ],
            scope.Sources
        );
        Assert.Equal([assigned], scope.AssignedCustomerIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Preview_and_download_follow_the_download_permission(bool canDownload)
    {
        var access = Access() with { CanDownload = canDownload };

        Assert.Equal(canDownload, LibraryAccessPolicy.CanPreview(access));
        Assert.Equal(canDownload, LibraryAccessPolicy.CanDownload(access));
    }

    [Fact]
    public void Assignment_restriction_is_reported_only_for_readable_restricted_sources()
    {
        Assert.False(LibraryAccessPolicy.AssignmentRestricted(Access(readable: [LibrarySource.Documents])));
        Assert.True(LibraryAccessPolicy.AssignmentRestricted(Access(readable: [LibrarySource.Financial])));
    }

    internal static LibraryAccess Access(
        LibrarySource[]? readable = null,
        bool viewAll = false,
        bool explorerRestrict = false,
        Guid[]? assigned = null
    ) => new(readable ?? AllSources, true, viewAll, explorerRestrict, assigned ?? []);
}
