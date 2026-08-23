using System.Reflection;

namespace Spectacle.Cli;

/// <summary>
/// The build identity <c>--version</c> reports.
///
/// <see cref="AssemblyName.Version"/> is four numbers the project never bumps, so it cannot answer
/// the question the flag is actually asked: is the installed exe the one built from this commit?
/// The build stamps the commit into <see cref="AssemblyInformationalVersionAttribute"/> instead —
/// an attribute rather than a file path, because <c>PublishSingleFile</c> leaves
/// <c>Assembly.Location</c> empty and anything reading the exe off disk would come back blank.
///
/// A source tree without <c>.git</c> still builds, and then the informational version carries no
/// revision — so the assembly version remains the fallback rather than the primary answer.
/// </summary>
public static class BuildStamp
{
    public static string Current => Describe(
        typeof(BuildStamp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion,
        typeof(BuildStamp).Assembly.GetName().Version?.ToString());

    /// <summary>
    /// Picks the more specific of the two, kept separate from the reflection so the choice is
    /// assertable without building a second assembly to read.
    /// </summary>
    public static string Describe(string? informationalVersion, string? assemblyVersion)
    {
        var informational = informationalVersion?.Trim();
        if (!string.IsNullOrEmpty(informational))
        {
            return informational;
        }

        var assembly = assemblyVersion?.Trim();
        return string.IsNullOrEmpty(assembly) ? "0.0.0" : assembly;
    }
}
