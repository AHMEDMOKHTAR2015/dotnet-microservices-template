using Microsoft.Extensions.Caching.Memory;

namespace Orders.Persistence;

public partial class OrdersDbContext(DbContextOptions<OrdersDbContext> options, IMemoryCache cache)
    : ApplicationDbContext<OrdersDbContext>(options, cache)
{
    #region Entities
    public virtual DbSet<Order> Orders { get; set; }
    public virtual DbSet<Stage> Stages { get; set; }
    public virtual DbSet<StageHistory> StageHistories { get; set; }
    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(this.GetType().Assembly);

        modelBuilder.UseEntityTypeNamesAsTables();       // table = singular CLR type name
    }

    public async override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        this.UnTrackCacheableEntities();                 // cached reference rows are never written back

        return await base.SaveChangesAsync(ct);
    }
}
