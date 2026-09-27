namespace Orders.Persistence.EntityConfigurations;

// Seeded from Data/Master/StageTransition.json by the base configuration.
public class StageTransitionConfiguration : MetadataConfiguration<StageTransition>
{
    public override void Configure(EntityTypeBuilder<StageTransition> builder)
    {
        base.Configure(builder);

        builder.HasKey(e => new { e.CurrentStage, e.ActionType, e.DestinationStage });

        builder.Property(e => e.CurrentStage).IsRequired().HasEnumConversion();
        builder.Property(e => e.ActionType).IsRequired().HasEnumConversion();
        builder.Property(e => e.DestinationStage).IsRequired().HasEnumConversion();
    }
}
