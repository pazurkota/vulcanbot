using Microsoft.EntityFrameworkCore;
using vulcanbot.Model;

namespace vulcanbot.Data;

public class VulcanDbContext(DbContextOptions<VulcanDbContext> options) : DbContext(options)
{
    public DbSet<VulcanAccount> Accounts => Set<VulcanAccount>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<Homework> Homeworks => Set<Homework>();
    public DbSet<Lesson> Lessons => Set<Lesson>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VulcanAccount>(e =>
        {
            e.HasMany(a => a.Exams).WithOne(x => x.Account).HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(a => a.Homeworks).WithOne(x => x.Account).HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(a => a.Lessons).WithOne(x => x.Account).HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Lesson>(e =>
        {
            e.Property(x => x.ChangeType).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.AccountId, x.Date });
        });

        modelBuilder.Entity<Exam>().HasIndex(x => new { x.AccountId, x.Deadline });
        modelBuilder.Entity<Homework>().HasIndex(x => new { x.AccountId, x.Deadline });
    }
}
