using CampusAdoptions.Models;

namespace CampusAdoptions.ViewModels;

public class CourseGroup
{
    public string CourseNumber { get; set; } = "";
    public string CourseName   { get; set; } = "";
    public List<CourseRequest> Sections { get; set; } = new();

    public bool HasNoRequest => !Sections.Any();

    public string Department =>
        CourseNumber.Contains(' ')
            ? CourseNumber.Split(' ')[0].Trim()
            : CourseNumber;

    public RequestStatus OverallStatus =>
        !Sections.Any()                                               ? RequestStatus.PendingVerification :
        Sections.All(s => s.Status == RequestStatus.Approved)        ? RequestStatus.Approved  :
        Sections.Any(s => s.Status == RequestStatus.Rejected)        ? RequestStatus.Rejected  :
        Sections.Any(s => s.Status == RequestStatus.Verified)        ? RequestStatus.Verified  :
        RequestStatus.PendingVerification;
}
