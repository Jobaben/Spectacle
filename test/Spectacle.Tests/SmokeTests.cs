using Xunit;
using FluentAssertions;

namespace Spectacle.Tests;

public class SmokeTests
{
    [Fact]
    public void ProjectsCompile() => Spectacle.Program.Main(Array.Empty<string>()).Should().Be(0);
}
