using Xunit;
using FluentAssertions;
using Spectacle.Files;

namespace Spectacle.Tests;

public class FileGuardTests
{
    [Theory]
    [InlineData("readme.md")]
    [InlineData("README.MD")]
    [InlineData("notes.markdown")]
    [InlineData("notes.MarkDown")]
    [InlineData(@"C:\path\to\file.md")]
    public void Accepts_markdown_extensions(string path) =>
        FileGuard.IsAllowed(path).Should().BeTrue();

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("script.ps1")]
    [InlineData("noext")]
    [InlineData("file.md.bak")]
    [InlineData("file.mdx")]
    public void Rejects_other_extensions(string path) =>
        FileGuard.IsAllowed(path).Should().BeFalse();

    [Fact]
    public void Rejects_null() =>
        FileGuard.IsAllowed(null!).Should().BeFalse();

    [Fact]
    public void Rejects_empty() =>
        FileGuard.IsAllowed("").Should().BeFalse();
}
