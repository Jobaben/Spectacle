using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class TableCheckerTests
{
    [Fact]
    public void Well_formed_table_has_no_issues()
    {
        const string content =
            "# T\n\n| Name | Type |\n|------|------|\n| id   | int  |\n| name | str  |\n";

        TableChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Row_with_extra_cell_is_flagged_at_its_line()
    {
        const string content =
            "| Name | Type |\n|------|------|\n| id | int | oops |\n";

        TableChecker.Check(content).Should().ContainSingle()
            .Which.Line.Should().Be(3);
    }

    [Fact]
    public void Row_with_too_few_cells_is_flagged()
    {
        const string content =
            "| A | B | C |\n|---|---|---|\n| 1 | 2 |\n";

        TableChecker.Check(content).Should().ContainSingle().Which.Line.Should().Be(3);
    }

    [Fact]
    public void Separator_column_mismatch_is_flagged()
    {
        const string content =
            "| A | B | C |\n|---|---|\n| 1 | 2 | 3 |\n";

        TableChecker.Check(content).Should().Contain(i => i.Line == 2);
    }

    [Fact]
    public void Tables_without_edge_pipes_are_handled()
    {
        const string content =
            "A | B\n--- | ---\n1 | 2\n";

        TableChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Escaped_pipes_do_not_count_as_columns()
    {
        const string content =
            "| Expr | Note |\n|------|------|\n| a \\| b | bitwise or |\n";

        TableChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Tables_inside_code_fences_are_ignored()
    {
        const string content =
            "```\n| A | B |\n|---|\n| 1 | 2 | 3 |\n```\n";

        TableChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Prose_without_tables_has_no_issues()
    {
        TableChecker.Check("# Title\n\nJust prose with a | stray pipe but no table.\n")
            .Should().BeEmpty();
    }
}
