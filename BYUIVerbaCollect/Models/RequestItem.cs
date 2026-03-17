using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BYUIVerbaCollect.Models;

public enum ItemType { Book, Supply }

public class RequestItem
{
    public int Id { get; set; }

    public int CourseRequestId { get; set; }
    [ForeignKey(nameof(CourseRequestId))]
    public CourseRequest CourseRequest { get; set; } = null!;

    public ItemType ItemType { get; set; } = ItemType.Book;

    // ── Book Fields ────────────────────────────────────────────────────────
    [MaxLength(300)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Author { get; set; }

    /// <summary>Auto-populated from Open Library API — no manual entry needed.</summary>
    [MaxLength(20)]
    public string? Isbn { get; set; }

    [MaxLength(200)]
    public string? Publisher { get; set; }

    [MaxLength(50)]
    public string? Edition { get; set; }

    public int? PublicationYear { get; set; }

    // ── Supply Fields ──────────────────────────────────────────────────────
    [MaxLength(300)]
    public string? SupplyDescription { get; set; }

    // ── Common Fields ──────────────────────────────────────────────────────
    public int Quantity { get; set; } = 1;
    public bool IsRequired { get; set; } = true;

    [MaxLength(500)]
    public string? Notes { get; set; }

    // ── Removal Request ────────────────────────────────────────────────────
    /// <summary>
    /// Set when a professor requests removal of an already-approved/verified item.
    /// The item stays in the DB until the material manager processes the removal.
    /// </summary>
    public DateTime? RemovalRequestedAt { get; set; }
}
