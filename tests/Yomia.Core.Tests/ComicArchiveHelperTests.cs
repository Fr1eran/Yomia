namespace Yomia.Core.Tests;

public class ComicArchiveHelperTests
{
    #region Items filtering test scenarios

    [Theory]
    // 1. Valid image formats
    [InlineData("001.jpg", true)]
    [InlineData("001.JPEG", true)]
    [InlineData("001.jxl", true)]
    [InlineData("page.png", true)]
    [InlineData("chapter/01.webp", true)]
    [InlineData("scan.avif", true)]
    [InlineData("cover.bmp", true)]
    [InlineData("animation.gif", true)]
    [InlineData("page.tif", true)]
    [InlineData("page.tiff", true)]
    // 2. Normal and backslash-separated directories
    [InlineData("Vol.01/", false)]
    [InlineData("Vol.01\\", false)]
    [InlineData("nested/dir/page/", false)]
    // 3. macOS shadow files and __MACOSX directory
    [InlineData("__MACOSX/Vol.01/01.jpg", false)]
    [InlineData("__MACOSX/._01.png", false)]
    [InlineData("Vol.01/._01.jpg", false)]
    [InlineData("._cover.png", false)]
    // 4. Double file extensions and disguised files
    [InlineData("virus.jpg.exe", false)]
    [InlineData("readme.png.txt", false)]
    [InlineData("archive.zip.rar", false)]
    // 5. Non-image files
    [InlineData("ComicInfo.xml", false)]
    [InlineData("Thumbs.db", false)]
    [InlineData("desktop.ini", false)]
    [InlineData(".DS_Store", false)]
    [InlineData("Folder/.DS_Store", false)]
    // 6. Blank and special invalid strings
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("no_extension_file", false)]
    public void IsValidImageEntry_ShouldFilterCorrectly(string? path, bool expected)
    {
        var actual = ComicArchiveHelper.IsValidImageEntry(path);
        Assert.Equal(expected, actual);
    }

    #endregion

    #region NaturalSort scenarios

    [Fact]
    public void NaturalSort_MultiLevelDirectories_ShouldPrioritizeDirectoryOverPageNumber()
    {
        var entries = new List<string>
        {
            "Vol.02/01.jpg",
            "Vol.01/10.jpg",
            "Vol.01/02.jpg",
            "Vol.01/01.jpg",
        };

        entries.Sort(ComicArchiveHelper.NaturalPathComparer);

        Assert.Equal("Vol.01/01.jpg", entries[0]);
        Assert.Equal("Vol.01/02.jpg", entries[1]);
        Assert.Equal("Vol.01/10.jpg", entries[2]);
        Assert.Equal("Vol.02/01.jpg", entries[3]);
    }

    [Fact]
    public void NaturalSort_SubPagesAndSpreads_ShouldSortInLogicalSequence()
    {
        var entries = new List<string>
        {
            "p006.jpg",
            "p005b.jpg",
            "p005a.jpg",
            "p005.jpg",
            "p004.jpg"
        };

        entries.Sort(ComicArchiveHelper.NaturalPathComparer);

        Assert.Equal("p004.jpg", entries[0]);
        Assert.Equal("p005.jpg", entries[1]);
        Assert.Equal("p005a.jpg", entries[2]);
        Assert.Equal("p005b.jpg", entries[3]);
        Assert.Equal("p006.jpg", entries[4]);
    }

    [Fact]
    public void NaturalSort_MixedCaseAndExtensions_ShouldSortPredictably()
    {
        var entries = new List<string>
        {
            "PAGE10.JPG",
            "page2.png",
            "Page1.WEBP"
        };
        entries.Sort(ComicArchiveHelper.NaturalPathComparer);
        Assert.Equal("Page1.WEBP", entries[0]);
        Assert.Equal("page2.png", entries[1]);
        Assert.Equal("PAGE10.JPG", entries[2]);
    }

    #endregion
}
