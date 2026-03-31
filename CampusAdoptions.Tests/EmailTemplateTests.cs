using CampusAdoptions.Services;

namespace CampusAdoptions.Tests;

public class EmailTemplateTests
{
    // ═══════════════════════════════════════════════════════════════════
    // HighPriceAlert
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void HighPriceAlert_SubjectContainsTitleAndPrice()
    {
        var (subject, _) = EmailTemplates.HighPriceAlert(
            "Prof Smith", "CSE 210", "Programming Book", "9781234567890", 85m);

        Assert.Contains("Programming Book", subject);
        Assert.Contains("$85.00", subject);
    }

    [Fact]
    public void HighPriceAlert_BodyContainsCourseNumber()
    {
        var (_, body) = EmailTemplates.HighPriceAlert(
            "Prof Smith", "CSE 210", "Programming Book", "9781234567890", 85m);

        Assert.Contains("CSE 210", body);
    }

    [Fact]
    public void HighPriceAlert_BodyContainsAffordabilityScore()
    {
        var (_, body) = EmailTemplates.HighPriceAlert(
            "Prof Smith", "CSE 210", "Programming Book", "9781234567890", 40m);

        // Affordability score for $40 = max(0, round(100 - 40 * 1.25)) = 50
        Assert.Contains("50 / 100", body);
    }

    [Fact]
    public void HighPriceAlert_WithAlternative_IncludesIt()
    {
        var (_, body) = EmailTemplates.HighPriceAlert(
            "Prof Smith", "CSE 210", "Expensive Book", "9781234567890", 100m,
            alternativeSuggestion: "Cheaper Edition (ISBN: 9780987654321) at $45");

        Assert.Contains("Suggested Alternative", body);
        Assert.Contains("Cheaper Edition", body);
    }

    [Fact]
    public void HighPriceAlert_WithoutAlternative_OmitsSection()
    {
        var (_, body) = EmailTemplates.HighPriceAlert(
            "Prof Smith", "CSE 210", "Expensive Book", "9781234567890", 100m);

        Assert.DoesNotContain("Suggested Alternative", body);
    }

    [Fact]
    public void HighPriceAlert_BodyContainsProfName()
    {
        var (_, body) = EmailTemplates.HighPriceAlert(
            "Prof Smith", "CSE 210", "Book", "9781234567890", 80m);

        Assert.Contains("Dear Prof Smith", body);
    }

    [Fact]
    public void HighPriceAlert_BodyContainsIsbn()
    {
        var (_, body) = EmailTemplates.HighPriceAlert(
            "Prof Smith", "CSE 210", "Book", "9781234567890", 80m);

        Assert.Contains("9781234567890", body);
    }

    // ═══════════════════════════════════════════════════════════════════
    // RequiredStatusChangeAlert
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RequiredStatusChangeAlert_RequiredToOptional_CorrectMessage()
    {
        var (subject, body) = EmailTemplates.RequiredStatusChangeAlert(
            "Prof Jones", "MATH 112", "Calculus I", "9781234567890",
            "Fall 2025", wasRequired: true, isNowRequired: false);

        Assert.Contains("Status Change", subject);
        Assert.Contains("was <strong>Required</strong>", body);
        Assert.Contains("now listed as <strong>Optional</strong>", body);
    }

    [Fact]
    public void RequiredStatusChangeAlert_OptionalToRequired_CorrectMessage()
    {
        var (_, body) = EmailTemplates.RequiredStatusChangeAlert(
            "Prof Jones", "MATH 112", "Calculus I", "9781234567890",
            "Fall 2025", wasRequired: false, isNowRequired: true);

        Assert.Contains("was <strong>Optional</strong>", body);
        Assert.Contains("now listed as <strong>Required</strong>", body);
    }

    [Fact]
    public void RequiredStatusChangeAlert_ContainsPreviousSemester()
    {
        var (_, body) = EmailTemplates.RequiredStatusChangeAlert(
            "Prof Jones", "MATH 112", "Calculus I", "9781234567890",
            "Fall 2025", wasRequired: true, isNowRequired: false);

        Assert.Contains("Fall 2025", body);
    }
}
