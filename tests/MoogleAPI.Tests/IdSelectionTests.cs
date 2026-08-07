using MoogleAPI.Scraper;

namespace MoogleAPI.Tests;

/// <summary>
/// <c>--ids</c> aims the two stages that throw work away or pay for it. The parse either produces
/// exactly the rows asked for or refuses to run, because the failure mode in between — reading a
/// malformed list as "no restriction" — turns a typo into a run against the whole library.
/// </summary>
public class IdSelectionTests
{
    [Fact]
    public void Absent_flag_means_no_restriction()
    {
        Assert.Null(IdSelection.Parse(null));
        Assert.Null(IdSelection.Parse(""));
        Assert.Null(IdSelection.Parse("   "));
    }

    [Fact]
    public void Separates_monsters_from_characters()
    {
        var ids = IdSelection.Parse("m31,c471,m52")!;

        Assert.Equal([31, 52], ids.Monsters.Order());
        Assert.Equal([471], ids.Characters.Order());
        Assert.Equal(3, ids.Count);
    }

    /// <summary>
    /// The two tables number from 1 independently, so the prefix is what makes an id mean
    /// anything: 25 is both Sarah and a monster.
    /// </summary>
    [Fact]
    public void Same_id_in_both_tables_is_two_different_rows()
    {
        var ids = IdSelection.Parse("m25,c25")!;

        Assert.Equal([25], ids.Monsters);
        Assert.Equal([25], ids.Characters);
        Assert.Equal(2, ids.Count);
    }

    [Theory]
    [InlineData("M31,C471")]
    [InlineData("m31 c471")]
    [InlineData("m31\nc471\n")]
    [InlineData(" m31 , c471 ")]
    public void Accepts_any_separator_and_either_case(string value)
    {
        var ids = IdSelection.Parse(value)!;

        Assert.Equal([31], ids.Monsters);
        Assert.Equal([471], ids.Characters);
    }

    [Fact]
    public void Duplicates_collapse()
    {
        Assert.Equal(1, IdSelection.Parse("m31,m31,m31")!.Count);
    }

    [Theory]
    [InlineData("31")]           // no folder prefix — ambiguous
    [InlineData("x31")]          // not a folder we have
    [InlineData("m")]            // prefix with no id
    [InlineData("mabc")]
    [InlineData("m0")]           // ids start at 1
    [InlineData("m-4")]
    public void Refuses_an_entry_it_cannot_read(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => IdSelection.Parse(value));
        Assert.Contains("--ids", ex.Message);
    }

    /// <summary>One bad entry fails the whole list rather than quietly dropping that row.</summary>
    [Fact]
    public void One_bad_entry_among_good_ones_still_throws()
    {
        Assert.Throws<ArgumentException>(() => IdSelection.Parse("m31,oops,c471"));
    }

    [Fact]
    public void A_value_naming_nothing_is_an_error_not_an_empty_selection()
    {
        Assert.Throws<ArgumentException>(() => IdSelection.Parse("# only a comment"));
    }

    [Fact]
    public void Reads_a_long_list_from_a_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "# the pixel-blocky monsters\nm31, m52\nm82\nc471\n");

            var ids = IdSelection.Parse("@" + path)!;

            Assert.Equal([31, 52, 82], ids.Monsters.Order());
            Assert.Equal([471], ids.Characters.Order());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
