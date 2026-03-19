using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CampusAdoptions.Models;

/// <summary>
/// Links an approved book ISBN to a course.
/// Stores the 5,000+ ISBN catalog — indexed for fast lookup.
/// </summary>
public class CourseBookAssignment
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    [ForeignKey(nameof(CourseId))]
    public Course Course { get; set; } = null!;

    /// <summary>ISBN-13 preferred; populated automatically from Open Library API.</summary>
    [Required, MaxLength(20)]
    public string Isbn { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Author { get; set; }

    [MaxLength(200)]
    public string? Publisher { get; set; }

    [MaxLength(50)]
    public string? Edition { get; set; }

    public int? PublicationYear { get; set; }

    public bool IsRequired { get; set; } = true;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public int? AssignedFromRequestId { get; set; }
}
