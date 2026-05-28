---
name: vertical-slice-architecture
description: Guide for implementing features using the vertical slice architecture with a three-project split. Use when creating features, endpoints, domain entities, database commands/queries, domain events, domain handlers, or DI registration.
---

## Architecture

Projects use a **three-project split**, but can vary in structure based on the service. The general pattern is:

- **`src/api/`**: Thin HTTP layer — endpoints, request/response contracts, validators, mappers. **No business logic.**
- **`src/service/`**: Core domain, features (business logic), database access, gateways, services, domain handlers.
- **`src/messaging/`**: Messaging layer — handles integration with external messaging systems. **No business logic.**

Code is organized by **feature folders**, not technical layers. All three projects mirror the same feature structure.

## Vertical Slice — Full Flow

When building a feature, follow this complete flow:

```
Request → Validator → Endpoint Group → Endpoint → Mapper → Feature → Domain Entity → Database Command → Domain Events → Domain Handlers
```

Not every feature needs all layers. Search the codebase to determine which are needed.

### 1. Request/Response Contracts (`src/api/Features/{Feature}/Contracts/`)

```csharp
public record MyRequest(string Name, decimal Amount);
public record MyResponse(string Id, string Status);
```

### 2. Validator (`src/api/Features/{Feature}/Contracts/RequestValidator.cs`)

```csharp
public class MyRequestValidator : AbstractValidator<MyRequest>
{
    public MyRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty();
        RuleFor(r => r.Amount).GreaterThan(0);
    }
}
```

### 3. Endpoint Group (`src/api/Features/{Feature}/{Feature}EndpointsGroup.cs`)

```csharp
public static class MyEndpointsGroup
{
    public static RouteGroupBuilder MapMyEndpoints(this RouteGroupBuilder parentGroup)
    {
        parentGroup.MapPost("myroute", MyEndpoint.Handle).WithName("MyAction");
        return parentGroup;
    }
}
```

Top-level groups set auth and FluentValidation:

```csharp
var group = endpoints.MapGroup("api/myarea")
    .RequireAuthorization(Policies.SystemAccess)
    .AddFluentValidationAutoValidation();
```

### 4. Endpoint (`src/api/Features/{Feature}/Endpoints/MyEndpoint.cs`)

Static class with static `Handle` method. DI via parameters. Returns `IResult`.

```csharp
public class MyEndpoint
{
    public static async Task<IResult> Handle(
        MyRequest request,
        MyFeature feature,
        IGetMyEntity getEntity,
        string id)
    {
        var entity = await getEntity.ById(id);
        if (entity == null) return Results.NotFound();

        var result = await feature.Execute(Mapper.ToCommand(entity, request));
        return result.Match(
            success => Results.Ok(Mapper.ToResponse(success)),
            error => error.ToResult()
        );
    }
}
```

### 5. Mapper (`src/api/Features/{Feature}/Mapper.cs`)

Static class mapping between request DTOs, domain objects, and feature commands/results.

```csharp
public class Mapper
{
    public static MyFeature.Command ToCommand(MyEntity entity, MyRequest request) =>
        new(entity, request.Name, request.Amount);

    public static MyResponse ToResponse(MyFeature.Result result) =>
        new(result.Id, result.Status);
}
```

### 6. Feature (`src/service/Features/{Feature}/MyFeature.cs`)

Plain scoped service with `Execute` method. Returns `OneOf<TSuccess, BookingErrorResult>` or `OneOf<Success, Error, NotValid>`. Uses **primary constructor** for DI. Inner `Command` and `Result` records define the contract.

```csharp
public class MyFeature(IGetMyEntity getEntity, IUpdateMyEntity updateEntity)
{
    public virtual async Task<OneOf<Result, BookingErrorResult>> Execute(Command command)
    {
        var entity = command.Entity;
        // business logic...
        await updateEntity.Execute(entity);
        return new Result(entity.Id, "Completed");
    }

    public record Command(MyEntity Entity, string Name, decimal Amount);
    public record Result(string Id, string Status);
}
```

### 7. Domain Entity (`src/service/Domain/`)

Rich model with private setters, factory methods, invariant enforcement, and domain events. Extends `DomainEntity` base class.

```csharp
public class MyEntity : DomainEntity
{
    private MyEntity(string id, string name) // private constructor
    {
        Ensure.That(id).IsNotNullOrEmpty();
        Id = id;
        Name = name;
    }

    public string Id { get; private set; }
    public string Name { get; private set; }

    public static MyEntity CreateNew(string id, string name)
    {
        var entity = new MyEntity(id, name);
        entity.RaiseDomainEvent(new MyEntityCreated(entity));
        return entity;
    }

    public static MyEntity CreateExisting(string id, string name) => new(id, name); // DB hydration, no event
}
```

V1 entities in `Domain/` are anemic EF classes for persistence — never add logic there.

### 8. Domain Events (`src/service/Domain/Events/`)

```csharp
public record MyEntityCreated(MyEntity Entity) : IDomainEvent;
```

### 9. Domain Handlers (`src/service/DomainHandlers/{EventName}/`)

```csharp
public class PublishMyEvent(IBookingEventPublisher eventPublisher) : INotificationHandler<Domain.Events.MyEntityCreated>
{
    public async Task Handle(Domain.Events.MyEntityCreated notification, CancellationToken cancellationToken)
    {
        await eventPublisher.Publish(new MyServiceBusEvent(notification.Entity.Id));
    }
}
```

### 10. Database Commands (`src/service/Database/Commands/`)

Interface + implementation in one file. EF save, domain event dispatch.

```csharp
public interface IInsertMyEntity { Task Execute(MyEntity entity); }

public class InsertMyEntity(AppDbContext dbContext, IMediator mediator) : IInsertMyEntity
{
    public async Task Execute(MyEntity entity)
    {
        await dbContext.MyEntities.AddAsync(entity);
        await dbContext.SaveChangesAsync();
        await mediator.DispatchDomainEvents(entity);
    }
}
```

### 11. Database Queries (`src/service/Database/Queries/`)

```csharp
public interface IGetMyEntity { Task<MyEntity?> ById(string id); }

public class GetMyEntity(AppDbContext db) : IGetMyEntity
{
    public async Task<MyEntity?> ById(string id) =>
        await db.MyEntities.FirstOrDefaultAsync(e => e.Id == id);
}
```

### 12. DI Registration (`ServicesConfiguration.cs`)

```csharp
public static IServiceCollection AddMyFeatures(this IServiceCollection services)
{
    services.AddScoped<IInsertMyEntity, InsertMyEntity>();
    services.AddScoped<MyFeature>();
    return services;
}
```

Register in the chain: `Database/ServicesConfiguration.cs` → `Features/ServicesConfiguration.cs` → `Infrastructure/ServicesConfiguration.cs` → `Api/Infrastructure/Services.cs`.

Also wire new endpoint groups into the parent group's `MapXxxEndpoints` method.

## Build & Verify

After making changes, always build and run tests:

```bash
dotnet build <solution>.slnx
dotnet test <solution>.slnx
```
