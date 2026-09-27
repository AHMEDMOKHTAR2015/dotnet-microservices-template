namespace Orders.Persistence.EntityConfigurations;

public class StageHistoryEntityConfiguration : EntityConfiguration<StageHistory>
{
    public override void Configure(EntityTypeBuilder<StageHistory> builder)
    {
        base.Configure(builder);

        builder.HasIndex(e => e.OrderId);

        builder.Property(e => e.StartDate).IsRequired();

        builder.HasOne<Stage>().WithMany().HasForeignKey(e => e.StageId).OnDelete(DeleteBehavior.Restrict);
    }
}
