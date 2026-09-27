# Starter.Grpc.Contracts

Code-first gRPC contracts (protobuf-net.Grpc), **one folder per owning service**, namespace `{OwningService}.Grpc`.

```csharp
// {OwningService}/{Name}Contracts.cs
namespace Customers.Grpc;

[ServiceContract]
public interface ICustomerService
{
    [OperationContract]
    ValueTask<GetCustomerResponse> GetCustomerByIdAsync(GetCustomerRequest request, CallContext context = default);
}

[ProtoContract] public class GetCustomerRequest  { [ProtoMember(1)] public int CustomerId { get; set; } }
[ProtoContract] public class GetCustomerResponse { [ProtoMember(1)] public CustomerInfo Customer { get; set; } = default!; }
```

- Server (owner): `services.AddCodeFirstGrpc(...)` + `app.MapGrpcService<CustomerGrpcService>()`.
- Client: `services.AddCodeFirstGrpcClient<ICustomerService>(config.GetSectionByTypeName<GrpcServicesOptions>(), "Customer")`.
- Use gRPC only to hydrate missing foreign data on a write path, or for an authoritative check at a transition.
  Never on read paths. Replicate descriptive data through integration events instead.
- Never renumber an existing `ProtoMember`.
