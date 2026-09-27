namespace Orders.Persistence.EntityConfigurations;

public class OrderLineEntityConfiguration : EntityConfiguration<OrderLine>
{
    public override void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        base.Configure(builder);

        builder.HasIndex(e => new { e.OrderId, e.ProductName }).IsUnique();

        builder.Property(e => e.ProductName).HasMaxLength(MaxLength.C128).IsRequired();
        builder.Property(e => e.UnitPrice).HasPrecision(18, 2);
    }
}
