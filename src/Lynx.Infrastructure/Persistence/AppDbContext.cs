using Lynx.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Lynx.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<RecurringItem> RecurringItems => Set<RecurringItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Account>(e =>
        {
            e.Property(a => a.Name).HasMaxLength(100).IsRequired();
            e.Property(a => a.Bank).HasMaxLength(100);
            e.Property(a => a.Iban).HasMaxLength(34);
            e.Property(a => a.Currency).HasMaxLength(3).IsRequired();
            e.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        });

        builder.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(60).IsRequired();
            e.Property(c => c.Color).HasMaxLength(7).IsRequired();
            e.HasIndex(c => c.Name).IsUnique();
            e.HasData(
                new Category { Id = 1, Name = "Groceries", Color = "#4f8cff" },
                new Category { Id = 2, Name = "Rent & Utilities", Color = "#8b5cf6" },
                new Category { Id = 3, Name = "Transport", Color = "#f59e0b" },
                new Category { Id = 4, Name = "Dining Out", Color = "#ef4444" },
                new Category { Id = 5, Name = "Subscriptions", Color = "#14b8a6" },
                new Category { Id = 6, Name = "Entertainment", Color = "#ec4899" },
                new Category { Id = 7, Name = "Health", Color = "#22c55e" },
                new Category { Id = 8, Name = "Salary", Color = "#10b981", IsIncome = true },
                new Category { Id = 9, Name = "Shopping", Color = "#f97316" });
        });

        builder.Entity<Budget>(e =>
        {
            e.Property(b => b.Month).HasMaxLength(7).IsRequired();
            e.Property(b => b.Currency).HasMaxLength(3).IsRequired();
            e.HasOne(b => b.Category).WithMany().HasForeignKey(b => b.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(b => new { b.Month, b.CategoryId, b.Currency }).IsUnique();
        });

        builder.Entity<RecurringItem>(e =>
        {
            e.Property(r => r.Payee).HasMaxLength(200).IsRequired();
            e.Property(r => r.PayeeKey).HasMaxLength(250).IsRequired();
            e.Property(r => r.Frequency).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(r => r.Account).WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.AccountId, r.PayeeKey }).IsUnique();
        });

        builder.Entity<Goal>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(100).IsRequired();
            e.Property(g => g.Currency).HasMaxLength(3).IsRequired();
            e.HasOne(g => g.Account).WithMany().HasForeignKey(g => g.AccountId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Transaction>(e =>
        {
            e.Property(t => t.Payee).HasMaxLength(200).IsRequired();
            e.Property(t => t.Description).HasMaxLength(500).IsRequired();
            e.Property(t => t.ImportKey).HasMaxLength(100);
            e.Property(t => t.CounterpartyIban).HasMaxLength(34);
            e.HasOne(t => t.Account).WithMany(a => a.Transactions).HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(t => new { t.AccountId, t.Date });
            e.HasIndex(t => new { t.AccountId, t.ImportKey }).IsUnique().HasFilter("\"ImportKey\" IS NOT NULL");
        });
    }
}
