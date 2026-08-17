#nullable enable
using CodeWalker.GameFiles;
using Duble.Core.Formats;
using Xunit;

namespace Duble.Tests;

public class CodeWalkerRuntimeTests
{
    [Fact]
    public void Initialize_puts_CodeWalker_in_gen9_reading_mode_and_can_be_called_twice()
    {
        CodeWalkerRuntime.Initialize();
        CodeWalkerRuntime.Initialize();
        Assert.True(RpfManager.IsGen9);
    }

    [Fact]
    public void Constructing_it_initialises_as_well_so_the_container_can_own_it()
    {
        Assert.NotNull(new CodeWalkerRuntime());
        Assert.True(RpfManager.IsGen9);
    }

    [Theory]
    [InlineData(true, "gen9")]
    [InlineData(false, "legacy")]
    [InlineData(null, "?")]
    public void FormatLabel_names_the_format_read_from_a_file_header(bool? gen9, string expected)
        => Assert.Equal(expected, CodeWalkerRuntime.FormatLabel(gen9));
}
