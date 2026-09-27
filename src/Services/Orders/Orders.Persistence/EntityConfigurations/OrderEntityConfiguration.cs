namespace Orders.Persistence.EntityConfigurations;

public class OrderEntityConfiguration : AuditedEntityConfiguration<Order>
{
    public override void Configure(EntityTypeBuilder<Order> builder)
    {
        base.Configure(builder);                                           // key, audit columns

        builder.Property(e => e.CustomerName).HasMaxLength(MaxLength.C128).IsRequired();
        builder.Property(e => e.Stage).HasEnumConversion().IsRequired();

        builder.ComplexProperty(e => e.CustomerEmail, vo =>              // value object = complex property
        {
            vo.Property(v => v.Value)
                .HasColumnName(vo.Metadata.PropertyInfo!.Name)
                .HasMaxLength(MaxLength.C64)
                .IsRequired();
        });

        builder.HasOne<Stage>().WithMany()                                 // FK to the EnumEntity by its name
            .HasForeignKey(e => e.Stage)
            .HasPrincipalKey(e => e.Name)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lines).WithOne()                            // children die with the aggregate
            .HasForeignKey(e => e.OrderId).IsRequired().OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.StageHistories).WithOne()
            .HasForeignKey(e => e.OrderId).IsRequired().OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Actions).WithOne()
            .HasForeignKey(e => e.OrderId).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }
}
