using System.Security.Claims;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;
using Compass.Bot.Infrastructure.Identity;
using Compass.Bot.Infrastructure.Members;
using Compass.Shared.Identity.Tokens;
using Microsoft.Extensions.Options;

namespace Compass.Bot.Application.Tests;

public sealed class IdentityResolverTests
{
    [Fact]
    public async Task Reads_person_from_the_validated_token_claims()
    {
        var resolver = new TeamsSsoPersonContextResolver(StaticValidator.ValidFor(
            new Claim("oid", "oid-1"), new Claim("tid", "tid-1"), new Claim("name", "Daniel Park")));

        var person = await resolver.ResolveAsync(new AccessToken("t"), CancellationToken.None);

        Assert.Equal(new PersonContext("oid-1", "tid-1", "Daniel Park"), person);
    }

    [Theory]
    [InlineData(TokenValidationStatus.Expired)]
    [InlineData(TokenValidationStatus.InvalidAudience)]
    [InlineData(TokenValidationStatus.InvalidIssuer)]
    [InlineData(TokenValidationStatus.InvalidSignature)]
    [InlineData(TokenValidationStatus.Malformed)]
    [InlineData(TokenValidationStatus.Missing)]
    public async Task Rejected_tokens_never_produce_a_person(TokenValidationStatus status)
    {
        var resolver = new TeamsSsoPersonContextResolver(new StaticValidator(new AccessTokenValidationResult(status)));

        await Assert.ThrowsAsync<PersonResolutionException>(() => resolver.ResolveAsync(new AccessToken("t"), CancellationToken.None));
    }

    [Theory]
    [InlineData("oid", "x")]
    [InlineData("tid", "x")]
    public async Task A_valid_token_without_oid_or_tid_is_rejected(string present, string value)
    {
        var resolver = new TeamsSsoPersonContextResolver(StaticValidator.ValidFor(new Claim(present, value)));

        await Assert.ThrowsAsync<PersonResolutionException>(() => resolver.ResolveAsync(new AccessToken("t"), CancellationToken.None));
    }

    [Fact]
    public void Member_number_comes_from_the_configured_directory_by_object_id()
    {
        var resolver = new ConfiguredMemberNumberResolver(Options.Create(new MemberDirectoryOptions
        {
            ByObjectId = { [TestPeople.UserA.ObjectId.ToUpperInvariant()] = "ZCU-100234" },
        }));

        Assert.Equal("ZCU-100234", resolver.Resolve(TestPeople.UserA));
    }

    [Fact]
    public void Member_number_is_found_by_an_object_id_without_hyphens()
    {
        var resolver = new ConfiguredMemberNumberResolver(Options.Create(new MemberDirectoryOptions
        {
            ByObjectId = { [TestPeople.UserA.ObjectId.Replace("-", string.Empty, StringComparison.Ordinal)] = "ZCU-100234" },
        }));

        Assert.Equal("ZCU-100234", resolver.Resolve(TestPeople.UserA));
    }

    [Fact]
    public void An_unlisted_person_has_no_member_number()
    {
        var resolver = new ConfiguredMemberNumberResolver(Options.Create(new MemberDirectoryOptions()));

        Assert.Throws<MemberNotLinkedException>(() => resolver.Resolve(TestPeople.UserB));
    }
}
