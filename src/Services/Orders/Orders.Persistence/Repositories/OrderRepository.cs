namespace Orders.Persistence.Repositories;

public class OrderRepository(OrdersDbContext dbContext) : Repository<Order>(dbContext)
{
    // Query() defines the aggregate boundary: every GetByIdAsync loads the order WITH its lines,
    // so invariants that span lines (duplicates, max count) see the whole collection.
    public override IQueryable<Order> Query()
        => base.Entity.Include(e => e.Lines);

    public async Task<Order?> GetFullOrderByIdAsync(int id, CancellationToken ct = default)
        => await Query()
            .Include(e => e.StageHistories)
            .AsSplitQuery()
            .SingleOrDefaultAsync(e => e.Id == id, ct);
}
