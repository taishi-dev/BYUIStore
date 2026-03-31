namespace CampusAdoptions.Models.Verba;

public class Material
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? IsbnAdopted { get; set; }
    public string Isbn13 { get; set; } = string.Empty;
    public string Isbn10 { get; set; } = string.Empty;
    public string? EIsbn { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string? Edition { get; set; }
    public string? PublicationDate { get; set; }
    public string? ListPrice { get; set; }
    public string? Binding { get; set; }
    public bool IsRequired { get; set; }
    public bool HasDigitalMatch { get; set; }
    public bool HasIaPrice { get; set; }
    public bool HasAccessibilityClaims { get; set; }
    public MaterialStatus Status { get; set; } = MaterialStatus.Required;
}

public enum MaterialStatus { Required, Optional, Recommended }
