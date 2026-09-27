namespace Orders.Persistence.EntityConfigurations;

public class OrderActionEntityConfiguration : EntityConfiguration<OrderAction>
{
    public override void Configure(EntityTypeBuilder<OrderAction> builder)
    {
        base.Configure(builder);

        builder.HasIndex(e => e.OrderId);

        builder.Property(e => e.ActionType).HasEnumConversion().IsRequired();
        builder.Property(e => e.Comment).HasMaxLength(MaxLength.C512);
    }
}
