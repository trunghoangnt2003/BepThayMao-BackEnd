using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Domain.Branches;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Branch> Branches => Set<Branch>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Branch>(branch =>
        {
            branch.ToTable("branches", table => table.HasCheckConstraint("ck_branches_open_before_close", "open_time < close_time"));
            branch.HasKey(value => value.Id);
            branch.Property(value => value.Id).ValueGeneratedNever();
            branch.Property(value => value.Name).HasMaxLength(200);
            branch.Property(value => value.Address).HasMaxLength(500);
            branch.Property(value => value.Phone).HasMaxLength(20);
        });
    }
    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => await SaveChangesAsync(cancellationToken);
}
