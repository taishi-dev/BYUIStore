using CampusAdoptions.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusAdoptions.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── Core workflow tables ───────────────────────────────────────────────
    public DbSet<AppUser>             Users             { get; set; }
    public DbSet<CourseRequest>       CourseRequests    { get; set; }
    public DbSet<RequestItem>         RequestItems      { get; set; }

    // ── University data tables (5,000+ ISBNs, 20,000+ students) ──────────
    public DbSet<Student>             Students          { get; set; }
    public DbSet<Course>              Courses           { get; set; }
    public DbSet<Enrollment>          Enrollments       { get; set; }
    public DbSet<CourseBookAssignment> CourseBookAssignments { get; set; }

    // ── Material review suggestions ─────────────────────────────────────
    public DbSet<MaterialSuggestion>  MaterialSuggestions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Indexes for high-volume lookups ────────────────────────────────

        // AppUser: fast login lookup
        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Username)
            .IsUnique();

        // Student: BYUI ID + email lookups (20,000+ rows)
        modelBuilder.Entity<Student>()
            .HasIndex(s => s.StudentId)
            .IsUnique();
        modelBuilder.Entity<Student>()
            .HasIndex(s => s.Email);

        // CourseBookAssignment: ISBN lookups (5,000+ rows)
        modelBuilder.Entity<CourseBookAssignment>()
            .HasIndex(b => b.Isbn);
        modelBuilder.Entity<CourseBookAssignment>()
            .HasIndex(b => b.CourseId);

        // Enrollment: composite index for student↔course joins
        modelBuilder.Entity<Enrollment>()
            .HasIndex(e => new { e.StudentId, e.CourseId })
            .IsUnique();

        // CourseRequest: status filter queries
        modelBuilder.Entity<CourseRequest>()
            .HasIndex(r => r.Status);
        modelBuilder.Entity<CourseRequest>()
            .HasIndex(r => r.SubmitterId);

        // ── Relationships ──────────────────────────────────────────────────

        modelBuilder.Entity<CourseRequest>()
            .HasOne(r => r.Submitter)
            .WithMany(u => u.SubmittedRequests)
            .HasForeignKey(r => r.SubmitterId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CourseRequest>()
            .HasOne(r => r.VerifiedBy)
            .WithMany()
            .HasForeignKey(r => r.VerifiedById)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CourseRequest>()
            .HasOne(r => r.ApprovedBy)
            .WithMany()
            .HasForeignKey(r => r.ApprovedById)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<RequestItem>()
            .HasOne(i => i.CourseRequest)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.CourseRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Enrollment>()
            .HasOne(e => e.Student)
            .WithMany(s => s.Enrollments)
            .HasForeignKey(e => e.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Enrollment>()
            .HasOne(e => e.Course)
            .WithMany(c => c.Enrollments)
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseBookAssignment>()
            .HasOne(b => b.Course)
            .WithMany(c => c.BookAssignments)
            .HasForeignKey(b => b.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // MaterialSuggestion: FK → RequestItem, cascade delete, indexed
        modelBuilder.Entity<MaterialSuggestion>()
            .HasOne(s => s.RequestItem)
            .WithMany(i => i.Suggestions)
            .HasForeignKey(s => s.RequestItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MaterialSuggestion>()
            .HasIndex(s => s.RequestItemId);
    }
}
