using BYUIVerbaCollect.Models;
using Microsoft.EntityFrameworkCore;

namespace BYUIVerbaCollect.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        // ── Seed Users ─────────────────────────────────────────────────────
        if (!await db.Users.AnyAsync())
        {
            var users = new List<AppUser>
            {
                new() { Username = "prof_smith",    PasswordHash = "byui1234", Role = "Professor",       FullName = "Prof. John Smith",    Department = "Computer Science", Email = "smith@byui.edu" },
                new() { Username = "coord_lee",     PasswordHash = "byui1234", Role = "Professor",       FullName = "Coord. Sarah Lee",    Department = "Business",         Email = "lee@byui.edu" },
                new() { Username = "manager_jones", PasswordHash = "byui1234", Role = "OfficeManager",   FullName = "Manager Amy Jones",   Department = "Academic Office",  Email = "jones@byui.edu" },
                new() { Username = "staff_brown",   PasswordHash = "byui1234", Role = "BookstoreStaff",  FullName = "Staff Tom Brown",     Department = "Bookstore",        Email = "brown@byui.edu" },
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

        // ── Seed a handful of Students (real app would import 20k+) ───────
        if (!await db.Students.AnyAsync())
        {
            var students = new List<Student>
            {
                new() { StudentId = "S100001", FirstName = "Alice",   LastName = "Johnson", Email = "alice.johnson@byui.edu",   Major = "Computer Science" },
                new() { StudentId = "S100002", FirstName = "Bob",     LastName = "Williams",Email = "bob.williams@byui.edu",    Major = "Business" },
                new() { StudentId = "S100003", FirstName = "Carol",   LastName = "Martinez",Email = "carol.martinez@byui.edu",  Major = "Mathematics" },
                new() { StudentId = "S100004", FirstName = "David",   LastName = "Taylor",  Email = "david.taylor@byui.edu",    Major = "Computer Science" },
                new() { StudentId = "S100005", FirstName = "Emma",    LastName = "Anderson",Email = "emma.anderson@byui.edu",   Major = "English" },
            };
            db.Students.AddRange(students);
            await db.SaveChangesAsync();
        }
    }
}
