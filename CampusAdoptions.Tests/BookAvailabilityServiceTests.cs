using CampusAdoptions.Services;

namespace CampusAdoptions.Tests;

public class BookAvailabilityServiceTests
{
    // ═══════════════════════════════════════════════════════════════════
    // Affordability Score: max(0, min(100, round(100 - price * 1.25)))
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0,    100)]  // Free = best
    [InlineData(40,   50)]   // $40 = medium
    [InlineData(80,   0)]    // $80 = worst
    [InlineData(200,  0)]    // Capped at 0
    [InlineData(60,   25)]   // $60 = 25
    [InlineData(20,   75)]   // $20 = 75
    public void AffordabilityScore_RetailPrice_CalculatesCorrectly(decimal price, int expected)
    {
        var item = new BookAvailabilityItem { PrintRetailPrice = price };
        Assert.Equal(expected, item.AffordabilityScore);
    }

    [Fact]
    public void AffordabilityScore_NullPrice_ReturnsNull()
    {
        var item = new BookAvailabilityItem();
        Assert.Null(item.AffordabilityScore);
    }

    [Fact]
    public void AffordabilityScore_FallsBackToListPrice()
    {
        var item = new BookAvailabilityItem { PrintListPrice = 40m };
        Assert.Equal(50, item.AffordabilityScore);
    }

    [Fact]
    public void AffordabilityScore_FallsBackToEbookPrice()
    {
        var item = new BookAvailabilityItem { EbookPrice = 40m };
        Assert.Equal(50, item.AffordabilityScore);
    }

    [Fact]
    public void AffordabilityScore_PrefersRetailOverList()
    {
        var item = new BookAvailabilityItem
        {
            PrintRetailPrice = 80m,  // → score 0
            PrintListPrice = 40m     // → would be 50, but retail takes priority
        };
        Assert.Equal(0, item.AffordabilityScore);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PriceFlaggedOver60
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(60.01, true)]
    [InlineData(100,   true)]
    [InlineData(60,    false)]
    [InlineData(59.99, false)]
    [InlineData(0,     false)]
    public void PriceFlaggedOver60_VariousRetailPrices(decimal price, bool expected)
    {
        var item = new BookAvailabilityItem { PrintRetailPrice = price };
        Assert.Equal(expected, item.PriceFlaggedOver60);
    }

    [Fact]
    public void PriceFlaggedOver60_NullPrice_ReturnsFalse()
    {
        var item = new BookAvailabilityItem();
        Assert.False(item.PriceFlaggedOver60);
    }

    [Fact]
    public void PriceFlaggedOver60_FallsBackToListPrice()
    {
        var item = new BookAvailabilityItem { PrintListPrice = 100m };
        Assert.True(item.PriceFlaggedOver60);
    }

    [Fact]
    public void PriceFlaggedOver60_FallsBackToEbookPrice()
    {
        var item = new BookAvailabilityItem { EbookPrice = 65m };
        Assert.True(item.PriceFlaggedOver60);
    }

    // ═══════════════════════════════════════════════════════════════════
    // HasIaPrice
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void HasIaPrice_AllNull_ReturnsFalse()
    {
        var item = new BookAvailabilityItem();
        Assert.False(item.HasIaPrice);
    }

    [Fact]
    public void HasIaPrice_RetailPriceSet_ReturnsTrue()
    {
        var item = new BookAvailabilityItem { PrintRetailPrice = 50m };
        Assert.True(item.HasIaPrice);
    }

    [Fact]
    public void HasIaPrice_ListPriceSet_ReturnsTrue()
    {
        var item = new BookAvailabilityItem { PrintListPrice = 50m };
        Assert.True(item.HasIaPrice);
    }

    [Fact]
    public void HasIaPrice_EbookPriceSet_ReturnsTrue()
    {
        var item = new BookAvailabilityItem { EbookPrice = 30m };
        Assert.True(item.HasIaPrice);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BookAvailabilityItem defaults
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void NewItem_HasCorrectDefaults()
    {
        var item = new BookAvailabilityItem();
        Assert.Equal("", item.Isbn);
        Assert.Equal("", item.Title);
        Assert.False(item.PrintAvailableOnGoogle);
        Assert.False(item.EbookAvailableOnGoogle);
        Assert.False(item.DigitalAvailableOnVitalSource);
        Assert.Null(item.VitalSourcePrice);
        Assert.Null(item.VitalSourcePriceDays);
        Assert.Equal(0, item.EnrollmentCount);
    }
}
