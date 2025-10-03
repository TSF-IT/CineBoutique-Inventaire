using System;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Endpoints;

public sealed class EndpointUtilitiesTests
{
    [Fact]
    public void GetOperatorContextWhenContextDoesNotContainEntryReturnsNull()
    {
        var httpContext = new DefaultHttpContext();

        var result = EndpointUtilities.GetOperatorContext(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public void GetOperatorContextWhenContextContainsEntryReturnsOperatorContext()
    {
        var httpContext = new DefaultHttpContext();
        var expected = new SoftOperatorMiddleware.OperatorContext(Guid.NewGuid(), "Op", "session-1");
        httpContext.Items[SoftOperatorMiddleware.OperatorContextItemKey] = expected;

        var result = EndpointUtilities.GetOperatorContext(httpContext);

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void ComposeAuditActorWithNoActorInformationReturnsNull()
    {
        var result = EndpointUtilities.ComposeAuditActor(null, operatorContext: null);

        result.Should().BeNull();
    }

    [Fact]
    public void ComposeAuditActorWithUserOnlyReturnsTrimmedUserName()
    {
        var result = EndpointUtilities.ComposeAuditActor("  john  ", operatorContext: null);

        result.Should().Be("john");
    }

    [Fact]
    public void ComposeAuditActorWithOperatorOnlyReturnsFormattedOperator()
    {
        var operatorId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var context = new SoftOperatorMiddleware.OperatorContext(operatorId, "Alice", "session-99");

        var result = EndpointUtilities.ComposeAuditActor(null, context);

        result.Should().Be($"operator:Alice ({operatorId:D}) session:session-99");
    }

    [Fact]
    public void ComposeAuditActorWithUserAndOperatorReturnsCombinedLabel()
    {
        var operatorId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-ffffffffffff");
        var context = new SoftOperatorMiddleware.OperatorContext(operatorId, null, null);

        var result = EndpointUtilities.ComposeAuditActor("  bob  ", context);

        result.Should().Be($"bob | operator:{operatorId:D}");
    }
}
