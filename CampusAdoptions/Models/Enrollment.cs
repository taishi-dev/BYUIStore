using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CampusAdoptions.Models;

/// <summary>
/// Junction table: a student enrolled in a course section.
/// Supports 20,000+ student records efficiently via indexed foreign keys.
/// </summary>
public class Enrollment
{
    public int Id { get; set; }

    public int StudentId { get; set; }
    [ForeignKey(nameof(StudentId))]
    public Student Student { get; set; } = null!;

    public int CourseId { get; set; }
    [ForeignKey(nameof(CourseId))]
    public Course Course { get; set; } = null!;

    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

    [MaxLength(20)]
    public string? Grade { get; set; }
}
