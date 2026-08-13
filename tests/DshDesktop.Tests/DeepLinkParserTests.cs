using DshDesktop.DeepLink;
using Xunit;

namespace DshDesktop.Tests;

public sealed class DeepLinkParserTests
{
    [Theory]
    [InlineData("dsh-desktop://")]
    [InlineData("dsh-desktop:")]
    [InlineData("dsh-desktop://launch")]
    [InlineData("DSH-DESKTOP://LAUNCH")]
    public void Parse_LaunchForms_ReturnLaunch(string arg)
    {
        var ok = DeepLinkRequest.TryParse(arg, out var request);

        Assert.True(ok);
        Assert.Equal(DeepLinkAction.Launch, request.Action);
        Assert.Null(request.SessionId);
    }

    [Fact]
    public void Parse_SessionForm_ReturnsSessionId()
    {
        var ok = DeepLinkRequest.TryParse("dsh-desktop://session/sess-42", out var request);

        Assert.True(ok);
        Assert.Equal(DeepLinkAction.Session, request.Action);
        Assert.Equal("sess-42", request.SessionId);
    }

    [Fact]
    public void Parse_SessionWithoutId_Fails()
    {
        Assert.False(DeepLinkRequest.TryParse("dsh-desktop://session/", out _));
        Assert.False(DeepLinkRequest.TryParse("dsh-desktop://session", out _));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dsh-desktop://unknown")]
    public void Parse_InvalidArgs_Fail(string arg)
    {
        Assert.False(DeepLinkRequest.TryParse(arg, out _));
    }
}
