namespace CampusAdoptions.Models.Verba;

public class Section
{
    public string Id { get; set; } = string.Empty;
    public string InstructorName { get; set; } = string.Empty;
    public bool HasStar { get; set; }
    public AdoptionStatus Status { get; set; } = AdoptionStatus.Approved;
    public List<Material> Materials { get; set; } = new();
}
