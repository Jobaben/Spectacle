namespace Spectacle.Web;

public enum NavDecision { AllowInPage, OpenInBrowser }

public static class LinkInterceptor
{
    public static NavDecision Decide(string currentUrl, string targetUrl)
    {
        if (string.IsNullOrEmpty(targetUrl)
            || targetUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || targetUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return NavDecision.AllowInPage;

        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var current)
            || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var target))
            return NavDecision.OpenInBrowser;

        var sameOrigin = string.Equals(current.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(current.Host, target.Host, StringComparison.OrdinalIgnoreCase)
                      && current.Port == target.Port;

        return sameOrigin ? NavDecision.AllowInPage : NavDecision.OpenInBrowser;
    }
}
