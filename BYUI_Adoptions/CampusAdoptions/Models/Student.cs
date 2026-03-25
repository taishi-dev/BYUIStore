using System.ComponentModel.DataAnnotations;

namespace CampusAdoptions.Models;

/// <summary>
/// Represents a BYUI student — supports 20,000+ records.
/// Indexed on StudentId and Email for fast lookups.
/// </summary>
public class Student
{
    public int Id { get; set; }

    /// <summary>BYUI-assigned student ID (e.g. "S123456")</summary>
    [Required, MaxLength(20)]
    public string StudentId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? Major { get; set; }

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();

    public string FullName => $"{FirstName} {LastName}";
}
