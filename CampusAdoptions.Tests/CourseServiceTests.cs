using CampusAdoptions.Models.Verba;
using CampusAdoptions.Services;

namespace CampusAdoptions.Tests;

public class CourseServiceTests
{
    private readonly CourseService _service = new();

    // ═══════════════════════════════════════════════════════════════════
    // GetCourses — filtering & search
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetCourses_NoFilters_ReturnsAllCourses()
    {
        var courses = _service.GetCourses();
        Assert.True(courses.Count > 0);
    }

    [Fact]
    public void GetCourses_DepartmentFilter_ReturnsOnlyThatDepartment()
    {
        var courses = _service.GetCourses(department: "ACCTG");
        Assert.All(courses, c => Assert.Equal("ACCTG", c.Department));
        Assert.True(courses.Count > 0);
    }

    [Fact]
    public void GetCourses_DepartmentALL_ReturnsAllCourses()
    {
        var all = _service.GetCourses();
        var filtered = _service.GetCourses(department: "ALL");
        Assert.Equal(all.Count, filtered.Count);
    }

    [Fact]
    public void GetCourses_SearchByCourseName_FindsMatch()
    {
        var courses = _service.GetCourses(searchTerm: "ACCOUNTING PRINCIPLES");
        Assert.Contains(courses, c => c.Name.Contains("ACCOUNTING PRINCIPLES"));
    }

    [Fact]
    public void GetCourses_SearchByCourseId_FindsMatch()
    {
        var courses = _service.GetCourses(searchTerm: "ACCTG-180");
        Assert.Contains(courses, c => c.Id == "ACCTG-180");
    }

    [Fact]
    public void GetCourses_SearchByInstructor_FindsMatch()
    {
        var courses = _service.GetCourses(searchTerm: "DUNLOP");
        Assert.True(courses.Count > 0);
        Assert.All(courses, c =>
            Assert.True(c.Sections.Any(s =>
                s.InstructorName.Contains("DUNLOP", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public void GetCourses_SearchIsCaseInsensitive()
    {
        var upper = _service.GetCourses(searchTerm: "CALCULUS");
        var lower = _service.GetCourses(searchTerm: "calculus");
        Assert.Equal(upper.Count, lower.Count);
    }

    [Fact]
    public void GetCourses_NoMatch_ReturnsEmpty()
    {
        var courses = _service.GetCourses(searchTerm: "XYZNONEXISTENT");
        Assert.Empty(courses);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetCourse — lookup by ID
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetCourse_ExistingId_ReturnsCourse()
    {
        var course = _service.GetCourse("ACCTG-100");
        Assert.NotNull(course);
        Assert.Equal("ACCTG-100", course.Id);
    }

    [Fact]
    public void GetCourse_CaseInsensitive()
    {
        var course = _service.GetCourse("acctg-100");
        Assert.NotNull(course);
        Assert.Equal("ACCTG-100", course.Id);
    }

    [Fact]
    public void GetCourse_NotFound_ReturnsNull()
    {
        var course = _service.GetCourse("NONEXISTENT-999");
        Assert.Null(course);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetNextCourseId — navigation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetNextCourseId_MiddleCourse_ReturnsNextId()
    {
        var nextId = _service.GetNextCourseId("ACCTG-100");
        Assert.NotNull(nextId);
    }

    [Fact]
    public void GetNextCourseId_LastCourse_ReturnsNull()
    {
        var allCourses = _service.GetCourses();
        var lastId = allCourses.Last().Id;
        var nextId = _service.GetNextCourseId(lastId);
        Assert.Null(nextId);
    }

    [Fact]
    public void GetNextCourseId_NotFound_ReturnsNull()
    {
        var nextId = _service.GetNextCourseId("NONEXISTENT-999");
        Assert.Null(nextId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetDepartments — distinct, sorted
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetDepartments_ReturnsDistinctValues()
    {
        var depts = _service.GetDepartments();
        Assert.Equal(depts.Count, depts.Distinct().Count());
    }

    [Fact]
    public void GetDepartments_IsSorted()
    {
        var depts = _service.GetDepartments();
        var sorted = depts.OrderBy(d => d).ToList();
        Assert.Equal(sorted, depts);
    }

    [Fact]
    public void GetDepartments_ContainsExpectedDepartments()
    {
        var depts = _service.GetDepartments();
        Assert.Contains("ACCTG", depts);
        Assert.Contains("MATH", depts);
        Assert.Contains("CIT", depts);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AddMaterial / RemoveMaterial
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AddMaterial_AddsToCourseSection()
    {
        var service = new CourseService(); // fresh instance
        var course = service.GetCourse("ACCTG-100")!;
        var section = course.Sections.First();
        var initialCount = section.Materials.Count;

        service.AddMaterial("ACCTG-100", section.Id, new Material
        {
            Title = "Test Book",
            Author = "Author",
            Isbn13 = "9999999999999",
            IsRequired = true
        });

        Assert.Equal(initialCount + 1, section.Materials.Count);
        Assert.Contains(section.Materials, m => m.Isbn13 == "9999999999999");
    }

    [Fact]
    public void RemoveMaterial_RemovesFromSection()
    {
        var service = new CourseService();
        var course = service.GetCourse("ACCTG-180")!;
        var section = course.Sections.First(s => s.Materials.Count > 0);
        var material = section.Materials.First(m => !string.IsNullOrEmpty(m.Isbn13));
        var isbn = material.Isbn13;
        var initialCount = section.Materials.Count;

        service.RemoveMaterial("ACCTG-180", section.Id, isbn);

        Assert.Equal(initialCount - 1, section.Materials.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetTermSummary
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetTermSummary_ReturnsNonZeroValues()
    {
        var summary = _service.GetTermSummary();
        Assert.True(summary.Approved > 0);
        Assert.True(summary.TotalCourses > 0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetMaterialsForAdoption
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetMaterialsForAdoption_ExistingInstructor_ReturnsMaterials()
    {
        var materials = _service.GetMaterialsForAdoption("SPRING 2026", "ACCTG-180", "STAFF");
        Assert.True(materials.Count > 0);
    }

    [Fact]
    public void GetMaterialsForAdoption_NonExistentInstructor_ReturnsEmpty()
    {
        var materials = _service.GetMaterialsForAdoption("SPRING 2026", "ACCTG-180", "NOBODY");
        Assert.Empty(materials);
    }
}
