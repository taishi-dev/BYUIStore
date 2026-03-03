using System.ComponentModel.DataAnnotations;

namespace BYUIVerbaCollect.Models;

/// <summary>
/// A course offered at BYUI with associated professor, schedule, and semester.
/// </summary>
public class Course
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string CourseNumber { get; set; } = string.Empty;   // e.g. "CSE 210"

    [Required, MaxLength(200)]
    public string CourseName { get; set; } = string.Empty;     // e.g. "Programming with Classes"

    [MaxLength(50)]
    public string? Section { get; set; }                       // e.g. "01"

    [Required, MaxLength(50)]
    public string Semester { get; set; } = string.Empty;       // e.g. "Winter 2026"

    // Schedule
    [MaxLength(20)]
    public string? DaysOfWeek { get; set; }                    // e.g. "MWF"

    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }

    [MaxLength(100)]
    public string? Room { get; set; }

    // Professor
    public int? ProfessorId { get; set; }

    [MaxLength(150)]
    public string? ProfessorName { get; set; }

    [MaxLength(200)]
    public string? Department { get; set; }

    public int MaxEnrollment { get; set; } = 30;

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<CourseBookAssignment> BookAssignments { get; set; } = new List<CourseBookAssignment>();
}
