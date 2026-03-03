using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BYUIVerbaCollect.Models;

public enum RequestStatus
{
    PendingVerification,
    Verified,
    Approved,
    Rejected
}

public class CourseRequest
{
    public int Id { get; set; }

    // ── Submitter ──────────────────────────────────────────────────────────
    public int SubmitterId { get; set; }
    [ForeignKey(nameof(SubmitterId))]
    public AppUser Submitter { get; set; } = null!;

    // ── Course Info ────────────────────────────────────────────────────────
    [Required, MaxLength(200)]
    public string CourseName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string CourseNumber { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Semester { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Section { get; set; }

    // ── Workflow Status ────────────────────────────────────────────────────
    public RequestStatus Status { get; set; } = RequestStatus.PendingVerification;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    // Verification
    public int? VerifiedById { get; set; }
    [ForeignKey(nameof(VerifiedById))]
    public AppUser? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }

    // Approval
    public int? ApprovedById { get; set; }
    [ForeignKey(nameof(ApprovedById))]
    public AppUser? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Rejection
    [MaxLength(500)]
    public string? RejectionNote { get; set; }

    // ── Items ──────────────────────────────────────────────────────────────
    public ICollection<RequestItem> Items { get; set; } = new List<RequestItem>();
}
