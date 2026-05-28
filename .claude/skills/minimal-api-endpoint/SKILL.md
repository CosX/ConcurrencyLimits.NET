---
name: minimal-api-endpoint
description: Use when creating or editing a Minimal API endpoint in an ASP.NET Core project. Covers the full vertical slice — endpoint handler, route group, feature, DTOs, validation, and DI registration.
---

# Create a Minimal API Endpoint

## Prerequisites

Before starting, gather context from the codebase:

1. **Find the central route registration** — look for a static class that calls `Map*Endpoints()` extension methods (e.g. a `MinimalApiConfiguration.cs` or similar).
2. **Find an existing endpoint group** — look for `MapGroup()` calls to understand route prefixes, auth policies, and validation setup.
3. **Find an existing endpoint handler** — look for static classes with a static `Handle` method.
4. **Find the DI registration file** — look for where `services.AddScoped<>()` calls are made for features.
5. **Find existing tests** — look for how API tests are structured (Alba, WebApplicationFactory, etc.).

Match the existing conventions exactly. Do NOT invent new patterns.

## Workflow

### Step 1: Use existing Feature or create a new Feature (Domain Layer)

Create the business logic feature in the domain project under `Features/{FeatureName}/`.

```csharp
public interface I{Verb}{Entity}Feature
{
    Task<OneOf<{SuccessType}, NotFound, Error>> Execute({parameters});
}

public class {Verb}{Entity}Feature({dependencies}) : I{Verb}{Entity}Feature
{
    public async Task<OneOf<{SuccessType}, NotFound, Error>> Execute({parameters})
    {
        // Implementation
    }
}
```

**Rules:**
- Use **primary constructors** for dependency injection
- Use **OneOf** return types for operations that can fail (not exceptions for flow control)
- Single `Execute` method per feature
- `public` class, `public` interface
- Features must NEVER call other features

### Step 2: Create Request/Response DTOs

**Responses** — prefer `record` for simple, immutable data:

```csharp
public record {Entity}Response(string Id, string Name, ...);
```

**Requests** — prefer `class` with nullable settable properties:

```csharp
public class {Verb}{Entity}Request
{
    public string? PropertyName { get; set; }
    public int? Amount { get; set; }
}
```

**Query parameters** — use a class with `[AsParameters]` for complex queries:

```csharp
public class {Entity}Query
{
    public string? HotelCode { get; set; }
    public DateOnly? FromDate { get; set; }
}
```

**Placement rules:**
- API-specific DTOs go in the API project: `Features/{Area}/Contracts/`
- Shared DTOs (used by domain + API) go in the domain project: `Contracts/`

### Step 3: Create the Endpoint Handler

Create a static class in `Features/{Area}/Endpoints/{Name}Endpoint.cs`:

```csharp
public class {Verb}{Entity}Endpoint
{
    public static async Task<IResult> Handle(
        I{Feature}Feature feature,        // DI services first
        string routeParam,                 // Route parameters
        [FromBody] RequestDto request)     // Body/query last
    {
        var result = await feature.Execute(routeParam, request);
        return result.Match(
            success => Results.Ok(success),
            notFound => Results.NotFound(),
            error => Results.Problem()
        );
    }
}
```

**Parameter binding conventions:**

| Source | Binding |
|--------|---------|
| DI services | First parameters, no attribute needed |
| Route params | Match `{name}` in route template |
| Request body | `[FromBody] RequestDto request` |
| Query string | `[FromQuery] string paramName` |
| Complex query | `[AsParameters] QueryDto query` |

**Return type mappings:**

| Result | HTTP |
|--------|------|
| `Results.Ok(data)` | 200 |
| `Results.Created(uri, data)` | 201 |
| `Results.NotFound()` | 404 |
| `Results.Problem()` | 500 |

### Step 4: Register the Route

#### Option A: Add to an existing endpoint group

If the feature area already has a group, add the route there:

```csharp
group.MapPost("resource/{id}", VerbEntityEndpoint.Handle)
    .WithName("VerbEntity");
```

#### Option B: Create a new endpoint group

If this is a new feature area, create `Features/{Area}/{Area}EndpointsGroup.cs`:

```csharp
public static class {Area}EndpointsGroup
{
    public static IEndpointRouteBuilder Map{Area}Endpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("api/{route-prefix}")
            .RequireAuthorization({policy})
            .AddFluentValidationAutoValidation();

        group.MapGet("", GetEntitiesEndpoint.Handle).WithName("GetEntities");
        group.MapPost("", CreateEntityEndpoint.Handle).WithName("CreateEntity");

        return routeBuilder;
    }
}
```

Then register it in the central route configuration:

```csharp
endpoints.Map{Area}Endpoints();
```

#### Nested sub-groups

For child groups under a parent, accept and return `RouteGroupBuilder`:

```csharp
public static RouteGroupBuilder Map{SubArea}Endpoints(this RouteGroupBuilder group)
{
    group.MapGet("sub-resource", GetSubEndpoint.Handle).WithName("GetSub");
    return group;
}
```

### Step 5: Register DI Services

Add feature registration in the DI configuration file:

```csharp
// Interface + implementation (preferred for testability)
services.AddScoped<IVerbEntityFeature, VerbEntityFeature>();

// Concrete class only (for simple features that don't need mocking)
services.AddScoped<GetEntitiesFeature>();
```

### Step 6: Add Validation (if needed)

Create a FluentValidation validator in the API project:

```csharp
public class {Verb}{Entity}RequestValidator : AbstractValidator<{Verb}{Entity}Request>
{
    public {Verb}{Entity}RequestValidator()
    {
        RuleFor(x => x.PropertyName).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
```

Validators are auto-discovered if `AddValidatorsFromAssembly()` is configured. The group's `.AddFluentValidationAutoValidation()` handles the rest.

## Naming Conventions

| Type | Pattern | Example |
|------|---------|---------|
| Feature interface | `I{Verb}{Entity}Feature` | `ICreateOrderFeature` |
| Feature class | `{Verb}{Entity}Feature` | `CreateOrderFeature` |
| Endpoint | `{Verb}{Entity}Endpoint` | `CreateOrderEndpoint` |
| Endpoint group | `{Area}EndpointsGroup` | `OrdersEndpointsGroup` |
| Request DTO | `{Verb}{Entity}Request` | `CreateOrderRequest` |
| Response DTO | `{Entity}Response` | `OrderResponse` |
| Validator | `{Verb}{Entity}RequestValidator` | `CreateOrderRequestValidator` |
