using Xunit;
using FluentAssertions;
using Spectacle.Web;

namespace Spectacle.Tests;

public class LinkInterceptorTests
{
    [Theory]
    [InlineData("https://spectacle.local/", "https://spectacle.local/#section", NavDecision.AllowInPage)]
    [InlineData("https://spectacle.local/", "https://spectacle.local/", NavDecision.AllowInPage)]
    [InlineData("https://spectacle.local/", "https://example.com/", NavDecision.OpenInBrowser)]
    [InlineData("https://spectacle.local/", "http://example.com/", NavDecision.OpenInBrowser)]
    [InlineData("https://spectacle.local/", "mailto:a@b.com", NavDecision.OpenInBrowser)]
    [InlineData("https://spectacle.local/", "file:///C:/x.md", NavDecision.OpenInBrowser)]
    [InlineData("https://spectacle.local/", "about:blank", NavDecision.AllowInPage)]
    [InlineData("https://spectacle.local/", "data:text/html,<p>hi</p>", NavDecision.AllowInPage)]
    // First render navigates while the WebView is still at about:blank; the
    // app's own virtual host must stay in-page regardless of current URL.
    [InlineData("about:blank", "https://spectacle.local/__spectacle_preview__.html?v=1", NavDecision.AllowInPage)]
    [InlineData("about:blank", "https://example.com/", NavDecision.OpenInBrowser)]
    public void Decides_navigation(string currentUrl, string targetUrl, NavDecision expected) =>
        LinkInterceptor.Decide(currentUrl, targetUrl).Should().Be(expected);
}
