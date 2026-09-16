using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Identity.Configurations;

/// <summary>
/// Move as tabelas do Identity para o schema proprio e limita o tamanho dos textos.
/// Nomes sem o prefixo AspNet: a plataforma e dona do schema, nao o framework.
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users", Fut7FantasyDbContext.IdentitySchema);
        builder.Property(user => user.DisplayName).HasMaxLength(80).IsRequired();
        builder.Property(user => user.CreatedAt).IsRequired();
    }
}

public sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Roles", Fut7FantasyDbContext.IdentitySchema);
    }
}

public sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) =>
        builder?.ToTable("UserClaims", Fut7FantasyDbContext.IdentitySchema);
}

public sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) =>
        builder?.ToTable("UserLogins", Fut7FantasyDbContext.IdentitySchema);
}

public sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) =>
        builder?.ToTable("UserTokens", Fut7FantasyDbContext.IdentitySchema);
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder) =>
        builder?.ToTable("UserRoles", Fut7FantasyDbContext.IdentitySchema);
}

public sealed class RoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder) =>
        builder?.ToTable("RoleClaims", Fut7FantasyDbContext.IdentitySchema);
}
