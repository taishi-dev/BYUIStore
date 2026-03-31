using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CampusAdoptions.Models;

public enum SuggestionType
{
    CheaperAlternative,
    NewerEdition,
    DigitalAvailable
}

public class MaterialSuggestion
{
    public int Id { get; set; }

    public int RequestItemId { get; set; }
    [ForeignKey(nameof(RequestItemId))]
    public RequestItem RequestItem { get; set; } = null!;

    public SuggestionType Type { get; set; }

    [MaxLength(20)]
    public string? SuggestedIsbn { get; set; }

    [MaxLength(300)]
    public string? SuggestedTitle { get; set; }

    [MaxLength(200)]
    public string? SuggestedAuthor { get; set; }

    [MaxLength(50)]
    public string? SuggestedEdition { get; set; }

    public int? SuggestedPublicationYear { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? SuggestedPrice { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? OriginalPrice { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? PriceSavings { get; set; }

    [MaxLength(500)]
    public string? SourceUrl { get; set; }

    [MaxLength(50)]
    public string? Source { get; set; }

    [MaxLength(500)]
    public string? ReasonText { get; set; }

    public bool DismissedByProfessor { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
