using CampusAdoptions.Models.Verba;

namespace CampusAdoptions.Services;

public class CourseService
{
    private readonly List<VerbaCourse> _courses;
    private readonly TermSummary _termSummary;

    public CourseService()
    {
        _courses = SeedCourses();
        _termSummary = new TermSummary(86, 74, 0, 3985, 1473);
    }

    public TermSummary GetTermSummary() => _termSummary;

    public List<VerbaCourse> GetCourses(string? searchTerm = null, string? department = null)
    {
        IEnumerable<VerbaCourse> result = _courses;

        if (!string.IsNullOrWhiteSpace(department) && department != "ALL")
            result = result.Where(c => c.Department.Equals(department, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(searchTerm))
            result = result.Where(c =>
                c.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                c.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                c.Sections.Any(s => s.InstructorName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));

        return result.ToList();
    }

    public VerbaCourse? GetCourse(string id) =>
        _courses.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public string? GetNextCourseId(string currentId)
    {
        var idx = _courses.FindIndex(c => c.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 && idx < _courses.Count - 1 ? _courses[idx + 1].Id : null;
    }

    public void AddMaterial(string courseId, string sectionId, Material material)
    {
        var course = GetCourse(courseId);
        var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
        section?.Materials.Add(material);
    }

    public void RemoveMaterial(string courseId, string sectionId, string isbn)
    {
        var course = GetCourse(courseId);
        var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
        var mat = section?.Materials.FirstOrDefault(m => m.Isbn13 == isbn || m.Isbn10 == isbn);
        if (mat != null) section!.Materials.Remove(mat);
    }

    public void UpdateMaterialStatus(string courseId, string sectionId, string isbn, MaterialStatus status)
    {
        var course = GetCourse(courseId);
        var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
        var mat = section?.Materials.FirstOrDefault(m => m.Isbn13 == isbn || m.Isbn10 == isbn);
        if (mat != null) mat.Status = status;
    }

    public List<Material> GetMaterialsForAdoption(string term, string courseId, string instructorId)
    {
        var course = GetCourse(courseId);
        var section = course?.Sections.FirstOrDefault(s =>
            s.InstructorName.Contains(instructorId, StringComparison.OrdinalIgnoreCase));
        return section?.Materials ?? new List<Material>();
    }

    public List<string> GetDepartments() =>
        _courses.Select(c => c.Department).Distinct().OrderBy(d => d).ToList();

    private static List<VerbaCourse> SeedCourses()
    {
        var courses = new List<VerbaCourse>();

        // ACCTG 100 — ACCOUNTING PRINCIPLES (4 sections, no text required)
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-100", Name = "ACCTG 100 - ACCOUNTING PRINCIPLES", Department = "ACCTG",
            SectionCount = 4, IsCurrentSinceExported = true, IsPushed = true, NoTextRequired = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "DUNLOP", Status = AdoptionStatus.Approved },
                new() { Id = "A2", InstructorName = "MCWHORTER", Status = AdoptionStatus.Approved },
                new() { Id = "A3", InstructorName = "MCWHORTER", Status = AdoptionStatus.Approved },
                new() { Id = "OL A1", InstructorName = "FOUTZ", Status = AdoptionStatus.Approved }
            }
        });

        // ACCTG 180 — SURVEY OF ACCOUNTING (2 sections, with materials)
        var acctg180Materials = new List<Material>
        {
            new()
            {
                Title = "ESTIMATED ADDITIONAL COURSE COSTS",
                Author = "NTR", Isbn13 = "", Isbn10 = "",
                Publisher = "", Edition = null, PublicationDate = null,
                ListPrice = null, Binding = "NTR",
                IsRequired = true, HasDigitalMatch = false, HasIaPrice = false,
                HasAccessibilityClaims = false, Status = MaterialStatus.Required
            },
            new()
            {
                Title = "SURVEY OF ACCOUNTING 6E / AUTO ACCESS",
                Author = "EDMONDS", IsbnAdopted = "9781264442614",
                Isbn13 = "9781264442614", Isbn10 = "1264442610",
                EIsbn = "9781264442621",
                Publisher = "MCGRAW HILL", Edition = "6TH",
                PublicationDate = "01/2020", ListPrice = "$149.50",
                Binding = "DIGITAL ACCESS", IsRequired = true,
                HasDigitalMatch = true, HasIaPrice = true,
                HasAccessibilityClaims = false, Status = MaterialStatus.Required
            }
        };
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-180", Name = "ACCTG 180 - SURVEY OF ACCOUNTING", Department = "ACCTG",
            SectionCount = 2, IsCurrentSinceExported = true, IsPushed = true,
            Sections = new List<Section>
            {
                new() { Id = "OL A2", InstructorName = "STAFF", Status = AdoptionStatus.Approved,
                    Materials = new List<Material>(acctg180Materials) },
                new() { Id = "A1", InstructorName = "WILSON JOHN DAVID", Status = AdoptionStatus.Approved,
                    Materials = new List<Material>(acctg180Materials) }
            }
        });

        // ACCTG 205 — INTERMEDIATE ACCOUNTING I
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-205", Name = "ACCTG 205 - INTERMEDIATE ACCOUNTING I", Department = "ACCTG",
            SectionCount = 3, IsCurrentSinceExported = true, IsPushed = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "HARRIS", Status = AdoptionStatus.Approved },
                new() { Id = "A2", InstructorName = "HARRIS", Status = AdoptionStatus.Approved },
                new() { Id = "OL A1", InstructorName = "JOHNSON", Status = AdoptionStatus.Approved }
            }
        });

        // ACCTG 210 — INTERMEDIATE ACCOUNTING II
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-210", Name = "ACCTG 210 - INTERMEDIATE ACCOUNTING II", Department = "ACCTG",
            SectionCount = 2, IsCurrentSinceExported = false, IsPushed = false,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "HARRIS", Status = AdoptionStatus.Submitted },
                new() { Id = "OL A1", InstructorName = "SMITH", Status = AdoptionStatus.Incomplete }
            }
        });

        // ACCTG 211 — COST ACCOUNTING
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-211", Name = "ACCTG 211 - COST ACCOUNTING", Department = "ACCTG",
            SectionCount = 3, IsCurrentSinceExported = true, IsPushed = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "BAKER", Status = AdoptionStatus.Approved },
                new() { Id = "A2", InstructorName = "BAKER", Status = AdoptionStatus.Approved },
                new() { Id = "OL A1", InstructorName = "NELSON", Status = AdoptionStatus.Approved }
            }
        });

        // ACCTG 233 — ACCOUNTING INFORMATION SYSTEMS
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-233", Name = "ACCTG 233 - ACCOUNTING INFORMATION SYSTEMS", Department = "ACCTG",
            SectionCount = 2, IsCurrentSinceExported = true, IsPushed = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "THOMPSON", Status = AdoptionStatus.Approved },
                new() { Id = "OL A1", InstructorName = "WILLIAMS", Status = AdoptionStatus.Approved }
            }
        });

        // ACCTG 299R — ACCOUNTING INTERNSHIP
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-299R", Name = "ACCTG 299R - ACCOUNTING INTERNSHIP", Department = "ACCTG",
            SectionCount = 1, IsCurrentSinceExported = true, IsPushed = true, NoTextRequired = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "DEPARTMENT", Status = AdoptionStatus.Approved }
            }
        });

        // ACCTG 301 — ADVANCED ACCOUNTING
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-301", Name = "ACCTG 301 - ADVANCED ACCOUNTING", Department = "ACCTG",
            SectionCount = 2, IsCurrentSinceExported = true, IsPushed = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "MCWHORTER", Status = AdoptionStatus.Approved },
                new() { Id = "OL A1", InstructorName = "DUNLOP", Status = AdoptionStatus.Approved }
            }
        });

        // ACCTG 302 — AUDITING
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-302", Name = "ACCTG 302 - AUDITING", Department = "ACCTG",
            SectionCount = 3, IsCurrentSinceExported = true, IsPushed = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "FOUTZ", Status = AdoptionStatus.Approved },
                new() { Id = "A2", InstructorName = "FOUTZ", Status = AdoptionStatus.Approved },
                new() { Id = "OL A1", InstructorName = "MILLER", Status = AdoptionStatus.Approved }
            }
        });

        // ACCTG 312 — GOVERNMENTAL ACCOUNTING
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-312", Name = "ACCTG 312 - GOVERNMENTAL ACCOUNTING", Department = "ACCTG",
            SectionCount = 2, IsCurrentSinceExported = true, IsPushed = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "ANDERSON", Status = AdoptionStatus.Approved },
                new() { Id = "OL A1", InstructorName = "ANDERSON", Status = AdoptionStatus.Approved }
            }
        });

        // ACCTG 321 — TAX ACCOUNTING
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-321", Name = "ACCTG 321 - TAX ACCOUNTING", Department = "ACCTG",
            SectionCount = 2, IsCurrentSinceExported = true, IsPushed = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "WILSON", Status = AdoptionStatus.Approved },
                new() { Id = "OL A1", InstructorName = "CLARK", Status = AdoptionStatus.Approved }
            }
        });

        // ACCTG 344 — ACCOUNTING CAPSTONE
        courses.Add(new VerbaCourse
        {
            Id = "ACCTG-344", Name = "ACCTG 344 - ACCOUNTING CAPSTONE", Department = "ACCTG",
            SectionCount = 1, IsCurrentSinceExported = true, IsPushed = true,
            Sections = new List<Section>
            {
                new() { Id = "A1", InstructorName = "DUNLOP", HasStar = true, Status = AdoptionStatus.Approved }
            }
        });

        // Additional departments to fill out the UI
        AddDepartmentCourses(courses, "ART", new[]
        {
            ("ART-100", "ART 100 - INTRODUCTION TO ART", 5),
            ("ART-101", "ART 101 - DRAWING FUNDAMENTALS", 3),
            ("ART-200", "ART 200 - ART HISTORY I", 4),
            ("ART-201", "ART 201 - ART HISTORY II", 2),
            ("ART-305", "ART 305 - PAINTING", 2),
        });

        AddDepartmentCourses(courses, "BIO", new[]
        {
            ("BIO-100", "BIO 100 - BIOLOGY FOUNDATIONS", 8),
            ("BIO-101", "BIO 101 - GENERAL BIOLOGY I", 6),
            ("BIO-102", "BIO 102 - GENERAL BIOLOGY II", 4),
            ("BIO-201", "BIO 201 - HUMAN ANATOMY", 5),
            ("BIO-202", "BIO 202 - HUMAN PHYSIOLOGY", 3),
            ("BIO-310", "BIO 310 - GENETICS", 2),
            ("BIO-350", "BIO 350 - MICROBIOLOGY", 3),
        });

        AddDepartmentCourses(courses, "CIT", new[]
        {
            ("CIT-111", "CIT 111 - INTRO TO COMPUTER SCIENCE", 6),
            ("CIT-160", "CIT 160 - INTRO TO PROGRAMMING", 5),
            ("CIT-225", "CIT 225 - DATABASE DESIGN", 3),
            ("CIT-260", "CIT 260 - OBJECT-ORIENTED PROGRAMMING", 4),
            ("CIT-325", "CIT 325 - WEB DEVELOPMENT", 3),
            ("CIT-365", "CIT 365 - SOFTWARE ENGINEERING", 2),
        });

        AddDepartmentCourses(courses, "ECON", new[]
        {
            ("ECON-150", "ECON 150 - MICROECONOMICS", 7),
            ("ECON-151", "ECON 151 - MACROECONOMICS", 6),
            ("ECON-300", "ECON 300 - MONEY AND BANKING", 2),
            ("ECON-388", "ECON 388 - ECONOMETRICS", 1),
        });

        AddDepartmentCourses(courses, "ENG", new[]
        {
            ("ENG-101", "ENG 101 - ENGLISH COMPOSITION", 12),
            ("ENG-201", "ENG 201 - TECHNICAL WRITING", 8),
            ("ENG-251", "ENG 251 - AMERICAN LITERATURE", 4),
            ("ENG-301", "ENG 301 - CREATIVE WRITING", 3),
        });

        AddDepartmentCourses(courses, "MATH", new[]
        {
            ("MATH-100", "MATH 100 - COLLEGE ALGEBRA", 10),
            ("MATH-101", "MATH 101 - PRECALCULUS", 8),
            ("MATH-112", "MATH 112 - CALCULUS I", 6),
            ("MATH-113", "MATH 113 - CALCULUS II", 4),
            ("MATH-215", "MATH 215 - LINEAR ALGEBRA", 3),
            ("MATH-325", "MATH 325 - DIFFERENTIAL EQUATIONS", 2),
        });

        return courses;
    }

    private static void AddDepartmentCourses(List<VerbaCourse> courses, string dept,
        (string id, string name, int sections)[] defs)
    {
        string[] instructors = { "SMITH", "JOHNSON", "WILLIAMS", "BROWN", "JONES",
            "DAVIS", "MILLER", "WILSON", "MOORE", "TAYLOR", "ANDERSON", "THOMAS" };
        int instrIdx = 0;

        foreach (var (id, name, sectionCount) in defs)
        {
            var sections = new List<Section>();
            for (int i = 0; i < sectionCount; i++)
            {
                var sectionId = i == 0 ? "A1" : i < sectionCount - 1 ? $"A{i + 1}" : $"OL A{i}";
                sections.Add(new Section
                {
                    Id = sectionId,
                    InstructorName = instructors[instrIdx % instructors.Length],
                    Status = AdoptionStatus.Approved
                });
                instrIdx++;
            }

            courses.Add(new VerbaCourse
            {
                Id = id, Name = name, Department = dept,
                SectionCount = sectionCount,
                IsCurrentSinceExported = true, IsPushed = true,
                Sections = sections
            });
        }
    }
}
