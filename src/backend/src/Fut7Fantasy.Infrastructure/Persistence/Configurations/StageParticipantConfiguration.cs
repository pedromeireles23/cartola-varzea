using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class StageParticipantConfiguration : IEntityTypeConfiguration<StageParticipant>
{
    public void Configure(EntityTypeBuilder<StageParticipant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("StageParticipants", Fut7FantasyDbContext.CompetitionsSchema);
        builder.HasKey(participant => participant.Id);
        builder.Property(participant => participant.Id).ValueGeneratedNever();

        builder.HasOne<Stage>().WithMany().HasForeignKey(participant => participant.StageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RealTeam>().WithMany().HasForeignKey(participant => participant.RealTeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StageGroup>().WithMany().HasForeignKey(participant => participant.StageGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(participant => new { participant.StageId, participant.RealTeamId }).IsUnique();
        builder.HasIndex(participant => participant.RealTeamId);
        builder.HasIndex(participant => participant.StageGroupId);
    }
}
