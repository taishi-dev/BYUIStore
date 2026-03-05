using System.ComponentModel.DataAnnotations;

namespace BYUIVerbaCollect.Models;

public class AppUser
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Roles: Professor | OfficeManager | BookstoreStaff | MaterialManager
    ///
    /// MaterialManager responsibilities:
    ///   1. Check if requested books are still available on VitalSource or Amazon.
    ///   2. Check cost — if price > $60, auto-email the professor to suggest cheaper alternatives.
    ///   3. Check required/optional changes — if a book was required last semester but is now
    ///      optional (or vice versa), auto-send a confirmation email to the professor.
    /// </summary>
    [Required, MaxLength(50)]
    public string Role { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Department { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    public ICollection<CourseRequest> SubmittedRequests { get; set; } = new List<CourseRequest>();
}
