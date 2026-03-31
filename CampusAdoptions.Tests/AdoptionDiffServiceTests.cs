using CampusAdoptions.Data;
using CampusAdoptions.Models;
using CampusAdoptions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace CampusAdoptions.Tests;

public class AdoptionDiffServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AdoptionDiffService _service;

    public AdoptionDiffServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        var logger = Mock.Of<ILogger<AdoptionDiffService>>();
        _service = new AdoptionDiffService(_db, logger);
    }

    public void Dispose() => _db.Dispose();

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private AppUser SeedUser()
    {
        var user = new AppUser
        {
            Username = "testuser",
            PasswordHash = "hash",
            Role = "MaterialManager",
            FullName = "Test User",
            Email = "test@byui.edu"
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private CourseRequest AddAdoption(
        AppUser submitter, string courseNumber, string semester,
        params (string isbn, string title, bool isRequired, string? edition)[] items)
    {
        var request = new CourseRequest
        {
            SubmitterId = submitter.Id,
            CourseNumber = courseNumber,
            CourseName = courseNumber,
            Semester = semester,
            Status = RequestStatus.Approved,
            SubmittedAt = DateTime.UtcNow,
            Items = items.Select(i => new RequestItem
            {
                ItemType = ItemType.Book,
                Isbn = i.isbn,
                Title = i.title,
                IsRequired = i.isRequired,
                Edition = i.edition
            }).ToList()
        };
        _db.CourseRequests.Add(request);
        _db.SaveChanges();
        return request;
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: No Changes
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Compare_IdenticalAdoptions_ReturnsNoDiffs()
    {
        var user = SeedUser();
        AddAdoption(user, "CSE 210", "Winter 2026",
            ("9781234567890", "Programming with Classes", true, "5th"));
        AddAdoption(user, "CSE 210", "Fall 2025",
            ("9781234567890", "Programming with Classes", true, "5th"));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        Assert.Equal(0, result.TotalChanges);
    }

    [Fact]
    public async Task Compare_EmptySemesters_ReturnsNoDiffs()
    {
        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");
        Assert.Equal(0, result.TotalChanges);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Material Added
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Compare_NewMaterialAdded_DetectsMaterialAdded()
    {
        var user = SeedUser();

        // Past: one book
        AddAdoption(user, "CSE 210", "Fall 2025",
            ("9781234567890", "Programming with Classes", true, "5th"));

        // Current: same book + a new one
        AddAdoption(user, "CSE 210", "Winter 2026",
            ("9781234567890", "Programming with Classes", true, "5th"),
            ("9780987654321", "Design Patterns", true, "1st"));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        Assert.Equal(1, result.MaterialsAdded);
        var diff = result.Diffs.Single(d => d.Type == DiffType.MaterialAdded);
        Assert.Equal("9780987654321", diff.CurrentIsbn);
        Assert.Equal("Design Patterns", diff.CurrentTitle);
    }

    [Fact]
    public async Task Compare_CourseNewThisSemester_AllMaterialsAreAdded()
    {
        var user = SeedUser();
        // No past adoption for this course

        AddAdoption(user, "ART 100", "Winter 2026",
            ("9781111111111", "Art History", true, null),
            ("9782222222222", "Drawing Basics", false, null));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        Assert.Equal(2, result.MaterialsAdded);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Material Removed
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Compare_MaterialDropped_DetectsMaterialRemoved()
    {
        var user = SeedUser();

        // Past: two books
        AddAdoption(user, "CSE 210", "Fall 2025",
            ("9781234567890", "Programming with Classes", true, "5th"),
            ("9780987654321", "Design Patterns", true, "1st"));

        // Current: only one book
        AddAdoption(user, "CSE 210", "Winter 2026",
            ("9781234567890", "Programming with Classes", true, "5th"));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        Assert.Equal(1, result.MaterialsRemoved);
        var diff = result.Diffs.Single(d => d.Type == DiffType.MaterialRemoved);
        Assert.Equal("9780987654321", diff.PastIsbn);
        Assert.Equal("Design Patterns", diff.PastTitle);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Required/Optional Status Changed
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Compare_RequiredToOptional_DetectsStatusChange()
    {
        var user = SeedUser();

        AddAdoption(user, "MATH 112", "Fall 2025",
            ("9781234567890", "Calculus I", true, "8th"));

        AddAdoption(user, "MATH 112", "Winter 2026",
            ("9781234567890", "Calculus I", false, "8th")); // Now optional

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        Assert.Equal(1, result.RequiredStatusChanges);
        var diff = result.Diffs.Single(d => d.Type == DiffType.RequiredStatusChanged);
        Assert.True(diff.PastIsRequired);
        Assert.False(diff.CurrentIsRequired);
    }

    [Fact]
    public async Task Compare_OptionalToRequired_DetectsStatusChange()
    {
        var user = SeedUser();

        AddAdoption(user, "ENG 101", "Fall 2025",
            ("9781234567890", "Writing Guide", false, null));

        AddAdoption(user, "ENG 101", "Winter 2026",
            ("9781234567890", "Writing Guide", true, null)); // Now required

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        Assert.Equal(1, result.RequiredStatusChanges);
        var diff = result.Diffs.Single(d => d.Type == DiffType.RequiredStatusChanged);
        Assert.False(diff.PastIsRequired);
        Assert.True(diff.CurrentIsRequired);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Edition Changed
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Compare_EditionUpgraded_DetectsEditionChange()
    {
        var user = SeedUser();

        AddAdoption(user, "ACCTG 180", "Fall 2025",
            ("9781264442614", "Survey of Accounting", true, "5th"));

        AddAdoption(user, "ACCTG 180", "Winter 2026",
            ("9781264442699", "Survey of Accounting", true, "6th")); // New edition, new ISBN

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        Assert.Equal(1, result.EditionChanges);
        var diff = result.Diffs.Single(d => d.Type == DiffType.EditionChanged);
        Assert.Equal("5th", diff.PastEdition);
        Assert.Equal("6th", diff.CurrentEdition);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Material Swapped (different ISBN, different title)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Compare_CompletelyDifferentBook_DetectsAddedAndRemoved()
    {
        var user = SeedUser();

        AddAdoption(user, "CIT 225", "Fall 2025",
            ("9781234567890", "Database Fundamentals", true, "3rd"));

        AddAdoption(user, "CIT 225", "Winter 2026",
            ("9780987654321", "SQL Performance Explained", true, "1st")); // Totally different book

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        // Different titles → shows as added + removed (not a swap)
        Assert.True(result.MaterialsAdded >= 1 || result.MaterialsRemoved >= 1);
    }

    [Fact]
    public async Task Compare_SameTitleDifferentIsbn_DetectsMaterialChanged()
    {
        var user = SeedUser();

        AddAdoption(user, "BIO 100", "Fall 2025",
            ("9781234567890", "Biology Foundations", true, null));

        AddAdoption(user, "BIO 100", "Winter 2026",
            ("9780987654321", "Biology Foundations", true, null)); // Same title, new ISBN

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        Assert.Equal(1, result.MaterialsChanged);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Multiple Changes at Once
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Compare_MultipleChanges_DetectsAll()
    {
        var user = SeedUser();

        // Past semester
        AddAdoption(user, "CSE 210", "Fall 2024",
            ("9781111111111", "Book A", true, "1st"),   // Will be removed
            ("9782222222222", "Book B", true, "2nd"));  // Status will change

        // Current semester
        AddAdoption(user, "CSE 210", "Winter 2026",
            ("9782222222222", "Book B", false, "2nd"),  // Required → Optional
            ("9783333333333", "Book C", true, "1st"));  // New

        var result = await _service.CompareAsync("Winter 2026", "Fall 2024");

        Assert.True(result.TotalChanges >= 3); // removed + status change + added
        Assert.True(result.MaterialsAdded >= 1);
        Assert.True(result.MaterialsRemoved >= 1);
        Assert.True(result.RequiredStatusChanges >= 1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Compare Against Multiple Past Semesters
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareAgainstHistory_MultiplePastSemesters_ReturnsResults()
    {
        var user = SeedUser();

        AddAdoption(user, "MATH 112", "Fall 2024",
            ("9781111111111", "Calculus I", true, "7th"));

        AddAdoption(user, "MATH 112", "Winter 2025",
            ("9781111111111", "Calculus I", true, "7th"));

        AddAdoption(user, "MATH 112", "Fall 2025",
            ("9782222222222", "Calculus I", true, "8th")); // Edition change in Fall 2025

        AddAdoption(user, "MATH 112", "Winter 2026",
            ("9782222222222", "Calculus I", false, "8th")); // Status change now

        var results = await _service.CompareAgainstHistoryAsync(
            "Winter 2026",
            new[] { "Fall 2025", "Winter 2025", "Fall 2024" });

        // vs Fall 2025: only status change (same ISBN)
        var vsFall2025 = results.FirstOrDefault(r => r.PastSemester == "Fall 2025");
        Assert.NotNull(vsFall2025);
        Assert.Equal(1, vsFall2025.RequiredStatusChanges);

        // vs Winter 2025: ISBN changed + status changed
        var vsWinter2025 = results.FirstOrDefault(r => r.PastSemester == "Winter 2025");
        Assert.NotNull(vsWinter2025);
        Assert.True(vsWinter2025.TotalChanges >= 1);

        // vs Fall 2024: ISBN changed + status changed
        var vsFall2024 = results.FirstOrDefault(r => r.PastSemester == "Fall 2024");
        Assert.NotNull(vsFall2024);
        Assert.True(vsFall2024.TotalChanges >= 1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Notification Routing
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DetermineNotifications_MaterialAdded_NotifiesProfessor()
    {
        var user = SeedUser();

        AddAdoption(user, "CSE 210", "Fall 2025",
            ("9781234567890", "Programming with Classes", true, "5th"));

        AddAdoption(user, "CSE 210", "Winter 2026",
            ("9781234567890", "Programming with Classes", true, "5th"),
            ("9780987654321", "Design Patterns", true, "1st"));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");
        var notifications = _service.DetermineNotifications(result);

        var addedNotification = notifications
            .FirstOrDefault(n => n.Diff.Type == DiffType.MaterialAdded);
        Assert.NotNull(addedNotification);
        Assert.Equal(NotificationRecipient.Professor, addedNotification.Recipient);
    }

    [Fact]
    public async Task DetermineNotifications_MaterialRemoved_NotifiesMaterialManager()
    {
        var user = SeedUser();

        AddAdoption(user, "CSE 210", "Fall 2025",
            ("9781234567890", "Programming with Classes", true, "5th"),
            ("9780987654321", "Design Patterns", true, "1st"));

        AddAdoption(user, "CSE 210", "Winter 2026",
            ("9781234567890", "Programming with Classes", true, "5th"));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");
        var notifications = _service.DetermineNotifications(result);

        var removedNotification = notifications
            .FirstOrDefault(n => n.Diff.Type == DiffType.MaterialRemoved);
        Assert.NotNull(removedNotification);
        Assert.Equal(NotificationRecipient.MaterialManager, removedNotification.Recipient);
    }

    [Fact]
    public async Task DetermineNotifications_RequiredStatusChanged_NotifiesProfessor()
    {
        var user = SeedUser();

        AddAdoption(user, "MATH 112", "Fall 2025",
            ("9781234567890", "Calculus I", true, "8th"));

        AddAdoption(user, "MATH 112", "Winter 2026",
            ("9781234567890", "Calculus I", false, "8th"));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");
        var notifications = _service.DetermineNotifications(result);

        var statusNotification = notifications
            .FirstOrDefault(n => n.Diff.Type == DiffType.RequiredStatusChanged);
        Assert.NotNull(statusNotification);
        Assert.Equal(NotificationRecipient.Professor, statusNotification.Recipient);
        Assert.Contains("Required", statusNotification.Reason);
        Assert.Contains("Optional", statusNotification.Reason);
    }

    [Fact]
    public async Task DetermineNotifications_EditionChanged_NotifiesCourseManager()
    {
        var user = SeedUser();

        AddAdoption(user, "ACCTG 180", "Fall 2025",
            ("9781264442614", "Survey of Accounting", true, "5th"));

        AddAdoption(user, "ACCTG 180", "Winter 2026",
            ("9781264442699", "Survey of Accounting", true, "6th"));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");
        var notifications = _service.DetermineNotifications(result);

        var editionNotification = notifications
            .FirstOrDefault(n => n.Diff.Type == DiffType.EditionChanged);
        Assert.NotNull(editionNotification);
        Assert.Equal(NotificationRecipient.CourseManager, editionNotification.Recipient);
    }

    [Fact]
    public async Task DetermineNotifications_MaterialSwap_NotifiesMaterialManager()
    {
        var user = SeedUser();

        AddAdoption(user, "BIO 100", "Fall 2025",
            ("9781234567890", "Biology Foundations", true, null));

        AddAdoption(user, "BIO 100", "Winter 2026",
            ("9780987654321", "Biology Foundations", true, null));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");
        var notifications = _service.DetermineNotifications(result);

        var swapNotification = notifications
            .FirstOrDefault(n => n.Diff.Type == DiffType.MaterialChanged);
        Assert.NotNull(swapNotification);
        Assert.Equal(NotificationRecipient.MaterialManager, swapNotification.Recipient);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Edge Cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Compare_RejectedRequests_AreExcluded()
    {
        var user = SeedUser();

        // Add a rejected request — should be ignored
        var rejected = new CourseRequest
        {
            SubmitterId = user.Id,
            CourseNumber = "CSE 210",
            CourseName = "CSE 210",
            Semester = "Fall 2025",
            Status = RequestStatus.Rejected,
            SubmittedAt = DateTime.UtcNow,
            Items = new List<RequestItem>
            {
                new() { ItemType = ItemType.Book, Isbn = "9781234567890",
                        Title = "Old Book", IsRequired = true }
            }
        };
        _db.CourseRequests.Add(rejected);
        _db.SaveChanges();

        AddAdoption(user, "CSE 210", "Winter 2026",
            ("9780987654321", "New Book", true, "1st"));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        // The rejected past adoption should not be compared
        // So "New Book" shows as added (no past baseline)
        Assert.Equal(1, result.MaterialsAdded);
    }

    [Fact]
    public async Task Compare_SupplyItems_AreIgnored()
    {
        var user = SeedUser();

        // Past: book + supply
        var pastRequest = new CourseRequest
        {
            SubmitterId = user.Id,
            CourseNumber = "ART 100",
            CourseName = "ART 100",
            Semester = "Fall 2025",
            Status = RequestStatus.Approved,
            SubmittedAt = DateTime.UtcNow,
            Items = new List<RequestItem>
            {
                new() { ItemType = ItemType.Book, Isbn = "9781111111111",
                        Title = "Art History", IsRequired = true },
                new() { ItemType = ItemType.Supply,
                        SupplyDescription = "Paint brushes", IsRequired = true }
            }
        };
        _db.CourseRequests.Add(pastRequest);
        _db.SaveChanges();

        AddAdoption(user, "ART 100", "Winter 2026",
            ("9781111111111", "Art History", true, null));

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        // Supply items should not generate diffs
        Assert.Equal(0, result.TotalChanges);
    }

    [Fact]
    public async Task Compare_IsbnWithDashes_NormalizedCorrectly()
    {
        var user = SeedUser();

        AddAdoption(user, "CSE 210", "Fall 2025",
            ("978-1-234-56789-0", "Programming Book", true, null));

        AddAdoption(user, "CSE 210", "Winter 2026",
            ("9781234567890", "Programming Book", true, null)); // Same ISBN, no dashes

        var result = await _service.CompareAsync("Winter 2026", "Fall 2025");

        Assert.Equal(0, result.TotalChanges); // Should be recognized as same ISBN
    }

    // ═══════════════════════════════════════════════════════════════════
    // TESTS: Two-Year Historical Comparison
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareAgainstHistory_TwoYearsBack_DetectsGradualChanges()
    {
        var user = SeedUser();

        // 2 years ago: Book A
        AddAdoption(user, "ECON 150", "Winter 2024",
            ("9781111111111", "Microeconomics", true, "10th"));

        // 1 year ago: Same book, now optional
        AddAdoption(user, "ECON 150", "Winter 2025",
            ("9781111111111", "Microeconomics", false, "10th"));

        // Current: New edition, required again
        AddAdoption(user, "ECON 150", "Winter 2026",
            ("9782222222222", "Microeconomics", true, "11th"));

        var results = await _service.CompareAgainstHistoryAsync(
            "Winter 2026",
            new[] { "Winter 2025", "Winter 2024" });

        // vs 1 year ago: edition change (title match, different ISBN + edition)
        var vsLastYear = results.First(r => r.PastSemester == "Winter 2025");
        Assert.True(vsLastYear.EditionChanges >= 1);

        // vs 2 years ago: edition change
        var vsTwoYears = results.First(r => r.PastSemester == "Winter 2024");
        Assert.True(vsTwoYears.EditionChanges >= 1);
    }
}
