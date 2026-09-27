using Blocks.Entities;

namespace Orders.Persistence.Repositories;

public class Repository<TEntity>(OrdersDbContext dbContext)
    : RepositoryBase<OrdersDbContext, TEntity>(dbContext)
    where TEntity : class, IEntity<int>;
