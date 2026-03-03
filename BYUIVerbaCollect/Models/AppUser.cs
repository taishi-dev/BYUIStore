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
    /// Roles: Professor | OfficeManager | BookstoreStaff
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
