namespace Orders.Domain.Orders;

//insight - enum + table: code stays readable (OrderStage.Draft) and the database stays joinable/descriptive.
// Seeded from Persistence/Data/Master/Stage.json.
public class Stage : EnumEntity<OrderStage>;
