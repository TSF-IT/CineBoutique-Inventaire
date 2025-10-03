using System;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Infrastructure.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class SoftOperatorMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithValidHeaders_ShouldStoreOperatorContext()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var operatorId = Guid.NewGuid();
        context.Request.Headers["X-Operator-Id"] = operatorId.ToString();
        context.Request.Headers["X-Operator-Name"] = "  Jane Doe  ";
        context.Request.Headers["X-Session-Id"] = "  session-42  ";

        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new SoftOperatorMiddleware(next, NullLogger<SoftOperatorMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context).ConfigureAwait(false);

        // Assert
        nextCalled.Should().BeTrue();
        context.Items.Should().ContainKey(SoftOperatorMiddleware.OperatorContextItemKey);
        context.Items[SoftOperatorMiddleware.OperatorContextItemKey]
            .Should().BeOfType<SoftOperatorMiddleware.OperatorContext>()
            .Which.Should().BeEquivalentTo(new SoftOperatorMiddleware.OperatorContext(operatorId, "Jane Doe", "session-42"));
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidOperatorId_ShouldIgnoreHeaders()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Operator-Id"] = "not-a-guid";
        context.Request.Headers["X-Operator-Name"] = "Ignored";
        context.Request.Headers["X-Session-Id"] = "ignored";

        var middleware = new SoftOperatorMiddleware(_ => Task.CompletedTask, NullLogger<SoftOperatorMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context).ConfigureAwait(false);

        // Assert
        context.Items.Should().NotContainKey(SoftOperatorMiddleware.OperatorContextItemKey);
    }
}
