using Bepthaymao.Domain.Branches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bepthaymao.Infrastructure.Persistence.Configurations;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches", table => table.HasCheckConstraint("ck_branches_open_before_close", "open_time < close_time"));
        builder.HasKey(branch => branch.Id);
        builder.Property(branch => branch.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(branch => branch.Name).HasColumnName("name").HasMaxLength(Branch.NameMaxLength).IsRequired();
        builder.Property(branch => branch.Address).HasColumnName("address").HasMaxLength(Branch.AddressMaxLength).IsRequired();
        builder.Property(branch => branch.Phone).HasColumnName("phone").HasMaxLength(Branch.PhoneMaxLength).IsRequired();
        builder.Property(branch => branch.OpenTime).HasColumnName("open_time").HasColumnType("time");
        builder.Property(branch => branch.CloseTime).HasColumnName("close_time").HasColumnType("time");
        builder.Property(branch => branch.IsActive).HasColumnName("is_active");
        builder.Property(branch => branch.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(branch => new { branch.IsActive, branch.Name });
    }
}
