---
name: feature-pattern
description: Use when creating, reviewing, or modifying Feature classes in a domain project. Covers structure, naming, folder layout, read/write patterns, DI registration, and anti-patterns.
---

# Feature Pattern

## Core Principles

1. **Single Responsibility** — one feature, one purpose
2. **One Public Method** — always named `Execute`
3. **Features NEVER call other Features** — use services/domain for composition
4. **Public interface, public implementation**

## Folder Structure

Place features in the domain project under `Features/{FeatureName}/`:

```
Features/
  GetCustomer/
    IGetCustomerFeature.cs
    GetCustomerFeature.cs
```

## Structure

```csharp
// Interface - exactly one Execute method
public interface IGetCustomerFeature
{
    Task<Customer?> Execute(CustomerId id);
}

// Implementation - public class with primary constructor
public class GetCustomerFeature(IDbContextFactory<AppDbContext> dbFactory) 
    : IGetCustomerFeature
{
    public async Task<Customer?> Execute(CustomerId id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id);
    }
}
```

## Naming

| Type | Convention | Example |
|------|------------|---------|
| Query | `IGet{Entity}Feature` | `IGetCustomerFeature` |
| List | `IGet{Entities}Feature` | `IGetCustomersFeature` |
| Command | `I{Action}{Entity}Feature` | `ICreateOrderFeature` |
| Validation | `IValidate{Entity}Feature` | `IValidateOrderFeature` |

## Read Features

- Inject the project's `DbContext`
- `AsNoTracking()` for read-only queries
- `AsSplitQuery()` when including multiple collections

## Write Features

- Inject the project's `DbContext`
- Load entity → apply domain logic → save changes
- `OneOf<T, NotFound, Error>` as return type for operations that can fail

## Registration

```csharp
services.AddScoped<IGetCustomerFeature, GetCustomerFeature>();
```

## ❌ Anti-Patterns

```csharp
// BAD: Feature calling another feature
public class CreateOrderFeature(IGetCustomerFeature getCustomer) { }

// BAD: Multiple public methods
public interface IBadFeature
{
    Task<Customer> GetById(int id);
    Task<List<Customer>> GetAll();  // Split into separate features!
}

// BAD: Named something other than Execute
public Task<Customer> GetCustomer(int id);  // Should be Execute()

// BAD: Void or fire-and-forget — always return a value or Task
public void Execute(int id);
public async void Execute(int id);  // Exceptions will be swallowed!
```
