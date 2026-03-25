using CampusAdoptions.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusAdoptions.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        // EnsureCreatedAsync is disabled for the shared SQL Server database.
        // The remote DB (TaishiOnly_CourseMaterials_dev) already contains the
        // full schema and historical semester data — calling EnsureCreated here
        // would attempt schema creation and could fail on existing tables.
        // Re-enable ONLY on a fresh local SQLite / dev database.
        // await db.Database.EnsureCreatedAsync();

        // ── Seed Users ─────────────────────────────────────────────────────
        if (!await db.Users.AnyAsync())
        {
            var users = new List<AppUser>
            {
                new() { Username = "prof_smith",    PasswordHash = "byui1234", Role = "Professor",       FullName = "Prof. John Smith",    Department = "Computer Science", Email = "smith@byui.edu" },
                new() { Username = "coord_lee",     PasswordHash = "byui1234", Role = "Professor",       FullName = "Coord. Sarah Lee",    Department = "Business",         Email = "lee@byui.edu" },
                new() { Username = "manager_jones", PasswordHash = "byui1234", Role = "OfficeManager",   FullName = "Manager Amy Jones",   Department = "Academic Office",  Email = "jones@byui.edu" },
                new() { Username = "staff_brown",   PasswordHash = "byui1234", Role = "MaterialManager", FullName = "Staff Tom Brown",     Department = "Bookstore",        Email = "brown@byui.edu" },
            };
            db.Users.AddRange(users);
            await db.SaveChangesAsync();
        }

        // ── Seed Courses ───────────────────────────────────────────────────
        if (!await db.Courses.AnyAsync())
        {
            var courses = new List<Course>
            {
                new() { CourseNumber = "CSE 210",  CourseName = "Programming with Classes",  Section = "01", Semester = "Winter 2026", DaysOfWeek = "MWF", StartTime = new TimeSpan(9, 0, 0),  EndTime = new TimeSpan(9, 50, 0),  Room = "STC 352",  ProfessorName = "Prof. John Smith",  Department = "Computer Science", MaxEnrollment = 30 },
                new() { CourseNumber = "CSE 212",  CourseName = "Programming with Data Structures", Section = "02", Semester = "Winter 2026", DaysOfWeek = "TTh", StartTime = new TimeSpan(10, 30, 0), EndTime = new TimeSpan(11, 45, 0), Room = "STC 354", ProfessorName = "Prof. John Smith", Department = "Computer Science", MaxEnrollment = 30 },
                new() { CourseNumber = "BUS 201",  CourseName = "Introduction to Business",  Section = "01", Semester = "Winter 2026", DaysOfWeek = "MWF", StartTime = new TimeSpan(11, 0, 0), EndTime = new TimeSpan(11, 50, 0), Room = "MC 102", ProfessorName = "Coord. Sarah Lee", Department = "Business", MaxEnrollment = 40 },
                new() { CourseNumber = "MATH 108", CourseName = "Mathematics for the Real World", Section = "03", Semester = "Winter 2026", DaysOfWeek = "TTh", StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(9, 15, 0), Room = "RKS 114", ProfessorName = "Dr. Linda Park", Department = "Mathematics", MaxEnrollment = 35 },
                new() { CourseNumber = "ENG 101",  CourseName = "Writing and Reasoning",     Section = "05", Semester = "Winter 2026", DaysOfWeek = "MWF", StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(14, 50, 0), Room = "RKS 210", ProfessorName = "Prof. Mark Davis", Department = "English", MaxEnrollment = 25 },
            };
            db.Courses.AddRange(courses);
            await db.SaveChangesAsync();
        }

        // ── Seed Students ──────────────────────────────────────────────────
        if (!await db.Students.AnyAsync())
        {
            var students = new List<Student>
            {
                new() { StudentId = "S100001", FirstName = "Alice",   LastName = "Johnson",  Email = "alice.johnson@byui.edu",   Major = "Computer Science" },
                new() { StudentId = "S100002", FirstName = "Bob",     LastName = "Williams", Email = "bob.williams@byui.edu",    Major = "Business" },
                new() { StudentId = "S100003", FirstName = "Carol",   LastName = "Martinez", Email = "carol.martinez@byui.edu",  Major = "Mathematics" },
                new() { StudentId = "S100004", FirstName = "David",   LastName = "Taylor",   Email = "david.taylor@byui.edu",    Major = "Computer Science" },
                new() { StudentId = "S100005", FirstName = "Emma",    LastName = "Anderson", Email = "emma.anderson@byui.edu",   Major = "English" },
                new() { StudentId = "S100006", FirstName = "Frank",   LastName = "Harris",   Email = "frank.harris@byui.edu",    Major = "Computer Science" },
                new() { StudentId = "S100007", FirstName = "Grace",   LastName = "Clark",    Email = "grace.clark@byui.edu",     Major = "Computer Science" },
                new() { StudentId = "S100008", FirstName = "Henry",   LastName = "Lewis",    Email = "henry.lewis@byui.edu",     Major = "Business" },
                new() { StudentId = "S100009", FirstName = "Isla",    LastName = "Walker",   Email = "isla.walker@byui.edu",     Major = "Mathematics" },
                new() { StudentId = "S100010", FirstName = "James",   LastName = "Hall",     Email = "james.hall@byui.edu",      Major = "English" },
            };
            db.Students.AddRange(students);
            await db.SaveChangesAsync();
        }

        // ── Seed Enrollments ───────────────────────────────────────────────
        if (!await db.Enrollments.AnyAsync())
        {
            var allStudents = await db.Students.ToListAsync();
            var allCourses  = await db.Courses.ToListAsync();

            var cse210  = allCourses.FirstOrDefault(c => c.CourseNumber == "CSE 210");
            var cse212  = allCourses.FirstOrDefault(c => c.CourseNumber == "CSE 212");
            var bus201  = allCourses.FirstOrDefault(c => c.CourseNumber == "BUS 201");
            var math108 = allCourses.FirstOrDefault(c => c.CourseNumber == "MATH 108");
            var eng101  = allCourses.FirstOrDefault(c => c.CourseNumber == "ENG 101");

            var enrollments = new List<Enrollment>();

            // CSE 210: 5 students
            if (cse210 != null)
                foreach (var s in allStudents.Take(5))
                    enrollments.Add(new() { StudentId = s.Id, CourseId = cse210.Id });

            // CSE 212: 4 students
            if (cse212 != null)
                foreach (var s in allStudents.Take(4))
                    enrollments.Add(new() { StudentId = s.Id, CourseId = cse212.Id });

            // BUS 201: 3 students
            if (bus201 != null)
                foreach (var s in allStudents.Skip(5).Take(3))
                    enrollments.Add(new() { StudentId = s.Id, CourseId = bus201.Id });

            // MATH 108: 4 students
            if (math108 != null)
                foreach (var s in allStudents.Skip(2).Take(4))
                    enrollments.Add(new() { StudentId = s.Id, CourseId = math108.Id });

            // ENG 101: 3 students
            if (eng101 != null)
                foreach (var s in allStudents.Skip(7).Take(3))
                    enrollments.Add(new() { StudentId = s.Id, CourseId = eng101.Id });

            db.Enrollments.AddRange(enrollments);
            await db.SaveChangesAsync();
        }

        // ── Seed Approved CourseRequests (Fall 2025) for Availability test ─
        if (!await db.CourseRequests.AnyAsync())
        {
            var submitter   = await db.Users.FirstOrDefaultAsync(u => u.Role == "Professor");
            var verifier    = await db.Users.FirstOrDefaultAsync(u => u.Role == "OfficeManager");
            var approver    = await db.Users.FirstOrDefaultAsync(u => u.Role == "MaterialManager");

            if (submitter != null && verifier != null && approver != null)
            {
                var now = DateTime.UtcNow;
                var requests = new List<CourseRequest>
                {
                    new()
                    {
                        SubmitterId  = submitter.Id, CourseNumber = "CSE 210",
                        CourseName   = "Programming with Classes", Section = "01",
                        Semester     = "Fall 2025",
                        Status       = RequestStatus.Approved,
                        SubmittedAt  = now.AddDays(-30),
                        VerifiedById = verifier.Id,  VerifiedAt  = now.AddDays(-28),
                        ApprovedById = approver.Id,  ApprovedAt  = now.AddDays(-25),
                        Items = new List<RequestItem>
                        {
                            new()
                            {
                                ItemType = ItemType.Book, IsRequired = true,
                                Isbn = "9780134685991",
                                Title = "Effective Java",
                                Author = "Joshua Bloch",
                                Publisher = "Addison-Wesley",
                                Edition = "3rd Edition",
                                PublicationYear = 2018,
                                Quantity = 1,
                                Notes = "Main textbook for the course"
                            }
                        }
                    },
                    new()
                    {
                        SubmitterId  = submitter.Id, CourseNumber = "CSE 212",
                        CourseName   = "Programming with Data Structures", Section = "02",
                        Semester     = "Fall 2025",
                        Status       = RequestStatus.Approved,
                        SubmittedAt  = now.AddDays(-30),
                        VerifiedById = verifier.Id,  VerifiedAt  = now.AddDays(-28),
                        ApprovedById = approver.Id,  ApprovedAt  = now.AddDays(-25),
                        Items = new List<RequestItem>
                        {
                            new()
                            {
                                ItemType = ItemType.Book, IsRequired = true,
                                Isbn = "9781491910740",
                                Title = "Learning Python",
                                Author = "Mark Lutz",
                                Publisher = "O'Reilly Media",
                                Edition = "5th Edition",
                                PublicationYear = 2013,
                                Quantity = 1,
                            },
                            new()
                            {
                                ItemType = ItemType.Book, IsRequired = false,
                                Isbn = "9780596516499",
                                Title = "JavaScript: The Good Parts",
                                Author = "Douglas Crockford",
                                Publisher = "O'Reilly Media",
                                Edition = "1st Edition",
                                PublicationYear = 2008,
                                Quantity = 1,
                                Notes = "Optional reference"
                            }
                        }
                    },
                    new()
                    {
                        SubmitterId  = submitter.Id, CourseNumber = "BUS 201",
                        CourseName   = "Introduction to Business", Section = "01",
                        Semester     = "Fall 2025",
                        Status       = RequestStatus.Approved,
                        SubmittedAt  = now.AddDays(-29),
                        VerifiedById = verifier.Id,  VerifiedAt  = now.AddDays(-27),
                        ApprovedById = approver.Id,  ApprovedAt  = now.AddDays(-24),
                        Items = new List<RequestItem>
                        {
                            new()
                            {
                                ItemType = ItemType.Book, IsRequired = true,
                                Isbn = "9781259929243",
                                Title = "Essentials of Business Communication",
                                Author = "Mary Ellen Guffey",
                                Publisher = "Cengage Learning",
                                Edition = "10th Edition",
                                PublicationYear = 2017,
                                Quantity = 1,
                            }
                        }
                    },
                    new()
                    {
                        SubmitterId  = submitter.Id, CourseNumber = "ENG 101",
                        CourseName   = "Writing and Reasoning", Section = "05",
                        Semester     = "Fall 2025",
                        Status       = RequestStatus.Approved,
                        SubmittedAt  = now.AddDays(-28),
                        VerifiedById = verifier.Id,  VerifiedAt  = now.AddDays(-26),
                        ApprovedById = approver.Id,  ApprovedAt  = now.AddDays(-23),
                        Items = new List<RequestItem>
                        {
                            new()
                            {
                                ItemType = ItemType.Book, IsRequired = true,
                                Isbn = "9780312601751",
                                Title = "The Bedford Handbook",
                                Author = "Diana Hacker",
                                Publisher = "Bedford/St. Martin's",
                                Edition = "8th Edition",
                                PublicationYear = 2010,
                                Quantity = 1,
                            }
                        }
                    },
                };
                db.CourseRequests.AddRange(requests);
                await db.SaveChangesAsync();
            }
        }

        // ── Ensure RM 342 (Spring 2025) is always present ─────────────────
        var hasRm342 = await db.CourseRequests
            .AnyAsync(c => c.CourseNumber == "RM 342" && c.Semester == "Spring 2025");

        if (!hasRm342)
        {
            var submitter = await db.Users.FirstOrDefaultAsync(u => u.Role == "Professor");
            var verifier  = await db.Users.FirstOrDefaultAsync(u => u.Role == "OfficeManager");
            var approver  = await db.Users.FirstOrDefaultAsync(u => u.Role == "MaterialManager");

            if (submitter != null && verifier != null && approver != null)
            {
                var now = DateTime.UtcNow;
                var rm342 = new CourseRequest
                {
                    SubmitterId  = submitter.Id,
                    CourseNumber = "RM 342",
                    CourseName   = "Resort Management",
                    Section      = "01",
                    Semester     = "Spring 2025",
                    Status       = RequestStatus.Approved,
                    SubmittedAt  = now.AddDays(-45),
                    VerifiedById = verifier.Id, VerifiedAt  = now.AddDays(-43),
                    ApprovedById = approver.Id, ApprovedAt  = now.AddDays(-40),
                    Items = new List<RequestItem>
                    {
                        new()
                        {
                            ItemType = ItemType.Book, IsRequired = true,
                            Isbn = "9781933108919",
                            Title = "Foundations of Resort Management",
                            Author = "Experimental Author A",
                            Publisher = "Resort Management Press",
                            Edition = "1st Edition",
                            PublicationYear = 2007,
                            Quantity = 30,
                            Notes = "Required main textbook"
                        },
                        new()
                        {
                            ItemType = ItemType.Book, IsRequired = true,
                            Isbn = "9781040439630",
                            Title = "Resort Operations and Guest Experience",
                            Author = "Experimental Author B",
                            Publisher = "Hospitality Academic Press",
                            Edition = "1st Edition",
                            PublicationYear = 2024,
                            Quantity = 30,
                            Notes = "Required supplemental text"
                        }
                    }
                };
                db.CourseRequests.Add(rm342);
                await db.SaveChangesAsync();
            }
        }
    }}
