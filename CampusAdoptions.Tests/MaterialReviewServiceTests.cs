using System.Reflection;
using System.Text.Json;
using CampusAdoptions.Services;

namespace CampusAdoptions.Tests;

public class MaterialReviewServiceTests
{
    // ═══════════════════════════════════════════════════════════════════
    // IsMeaningfullyCheaper: savings >= $10 OR savings >= 15%
    // ═══════════════════════════════════════════════════════════════════

    // Access the private static method via reflection
    private static bool IsMeaningfullyCheaper(decimal original, decimal suggested)
    {
        var method = typeof(MaterialReviewService)
            .GetMethod("IsMeaningfullyCheaper", BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method!.Invoke(null, new object[] { original, suggested })!;
    }

    [Theory]
    [InlineData(100, 89, true)]   // $11 savings >= $10
    [InlineData(100, 90, true)]   // $10 savings >= $10
    [InlineData(100, 91, false)]  // $9 savings < $10, 9% < 15%
    [InlineData(100, 92, false)]  // $8 savings < $10, 8% < 15%
    [InlineData(40, 33, true)]    // $7 savings < $10, but 17.5% >= 15%
    [InlineData(40, 34, true)]    // $6 savings < $10, but 15% >= 15%
    [InlineData(40, 36, false)]   // $4 savings < $10, 10% < 15%
    [InlineData(200, 170, true)]  // $30 savings — both rules pass
    [InlineData(50, 50, false)]   // Zero savings
    [InlineData(10, 1, true)]     // $9 savings, 90% >= 15%
    public void IsMeaningfullyCheaper_VariousScenarios(decimal original, decimal suggested, bool expected)
    {
        Assert.Equal(expected, IsMeaningfullyCheaper(original, suggested));
    }

    // ═══════════════════════════════════════════════════════════════════
    // NormalizeTitle: strips subtitles, editions, parentheticals
    // ═══════════════════════════════════════════════════════════════════

    private static string NormalizeTitle(string title)
    {
        var method = typeof(MaterialReviewService)
            .GetMethod("NormalizeTitle", BindingFlags.NonPublic | BindingFlags.Static);
        return (string)method!.Invoke(null, new object[] { title })!;
    }

    [Fact]
    public void NormalizeTitle_RemovesSubtitle()
    {
        var result = NormalizeTitle("Calculus I: Foundations of Mathematics");
        Assert.Equal("calculus i", result);
    }

    [Fact]
    public void NormalizeTitle_RemovesEdition()
    {
        var result = NormalizeTitle("Biology 5th edition");
        Assert.Equal("biology", result);
    }

    [Fact]
    public void NormalizeTitle_RemovesParentheticals()
    {
        var result = NormalizeTitle("Programming (with Java)");
        Assert.Equal("programming", result);
    }

    [Fact]
    public void NormalizeTitle_Lowercases()
    {
        var result = NormalizeTitle("SURVEY OF ACCOUNTING");
        Assert.Equal("survey of accounting", result);
    }

    [Fact]
    public void NormalizeTitle_CombinedTransforms()
    {
        var result = NormalizeTitle("Calculus I: Foundations (3rd Edition)");
        Assert.Equal("calculus i", result);
    }

    [Fact]
    public void NormalizeTitle_TrimsWhitespace()
    {
        var result = NormalizeTitle("  Biology  ");
        Assert.Equal("biology", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TitlesMatch: exact, substring, empty, null
    // ═══════════════════════════════════════════════════════════════════

    private static bool TitlesMatch(string? a, string? b)
    {
        var method = typeof(MaterialReviewService)
            .GetMethod("TitlesMatch", BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method!.Invoke(null, new object?[] { a, b })!;
    }

    [Fact]
    public void TitlesMatch_ExactMatch_ReturnsTrue()
    {
        Assert.True(TitlesMatch("calculus", "calculus"));
    }

    [Fact]
    public void TitlesMatch_SubstringMatch_ReturnsTrue()
    {
        Assert.True(TitlesMatch("calculus i", "calculus"));
        Assert.True(TitlesMatch("calculus", "calculus i"));
    }

    [Fact]
    public void TitlesMatch_NullA_ReturnsFalse()
    {
        Assert.False(TitlesMatch(null, "calculus"));
    }

    [Fact]
    public void TitlesMatch_NullB_ReturnsFalse()
    {
        Assert.False(TitlesMatch("calculus", null));
    }

    [Fact]
    public void TitlesMatch_BothEmpty_ReturnsFalse()
    {
        Assert.False(TitlesMatch("", ""));
    }

    [Fact]
    public void TitlesMatch_DifferentTitles_ReturnsFalse()
    {
        Assert.False(TitlesMatch("calculus", "biology"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // ExtractIsbn13: prefers ISBN-13, fallback to ISBN-10
    // ═══════════════════════════════════════════════════════════════════

    private static string? ExtractIsbn13(JsonElement volumeInfo)
    {
        var method = typeof(MaterialReviewService)
            .GetMethod("ExtractIsbn13", BindingFlags.NonPublic | BindingFlags.Static);
        return (string?)method!.Invoke(null, new object[] { volumeInfo });
    }

    [Fact]
    public void ExtractIsbn13_PrefersIsbn13()
    {
        var json = """{"industryIdentifiers":[{"type":"ISBN_10","identifier":"1234567890"},{"type":"ISBN_13","identifier":"9781234567890"}]}""";
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("9781234567890", ExtractIsbn13(doc.RootElement));
    }

    [Fact]
    public void ExtractIsbn13_FallsBackToIsbn10()
    {
        var json = """{"industryIdentifiers":[{"type":"ISBN_10","identifier":"1234567890"}]}""";
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("1234567890", ExtractIsbn13(doc.RootElement));
    }

    [Fact]
    public void ExtractIsbn13_NoIdentifiers_ReturnsNull()
    {
        var json = """{"title":"Test"}""";
        using var doc = JsonDocument.Parse(json);
        Assert.Null(ExtractIsbn13(doc.RootElement));
    }

    // ═══════════════════════════════════════════════════════════════════
    // ParseYear: extracts 4-digit year from various formats
    // ═══════════════════════════════════════════════════════════════════

    private static int? ParseYear(string? dateStr)
    {
        var method = typeof(MaterialReviewService)
            .GetMethod("ParseYear", BindingFlags.NonPublic | BindingFlags.Static);
        return (int?)method!.Invoke(null, new object?[] { dateStr });
    }

    [Theory]
    [InlineData("2024", 2024)]
    [InlineData("2024-01-15", 2024)]
    [InlineData("January 2024", 2024)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("no year here", null)]
    public void ParseYear_VariousFormats(string? input, int? expected)
    {
        Assert.Equal(expected, ParseYear(input));
    }
}
