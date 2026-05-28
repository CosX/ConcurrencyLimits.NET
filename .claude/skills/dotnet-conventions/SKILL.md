---
name: dotnet-conventions
description: Naming conventions and code style rules for this project. Use when writing or reviewing any .NET code in this repository.
---

## Naming

| Thing | Convention | Example |
|-------|-----------|---------|
| Feature class | `XxxFeature` | `HoldStayFeature` |
| Endpoint class | `XxxEndpoint` | `CreateStayEndpoint` |
| Endpoint group | `XxxEndpointsGroup` | `BookingEndpointsGroup` |
| DTOs | `XxxDto` | `StayDto` |
| Request/Response | `XxxRequest` / `XxxResponse` | `CreateStayRequest` |
| Validator | `XxxRequestValidator` or `RequestValidator` | `CancelStayRequestValidator` |
| Interfaces | `I` prefix | `IGetStay`, `IInsertStay` |
| Value objects | Records | `Money`, `StayPeriod` |
| Domain events | Past tense records | `StayCreated`, `RoomCanceled` |
| DB commands | `IInsertXxx`, `IUpdateXxx`, `IDeleteXxx` | `IInsertStay` |
| DB queries | `IGetXxx`, `ILoadXxx` | `IGetStay` |

## Code Style

- **Primary constructors** for DI injection — no explicit backing fields. Applies to all DI-resolved types: features, services, handlers, middleware, transform providers, hosted services, validators. Reference the parameter directly (`logger.LogInformation(...)`), never alias to `_logger`. Exception: types whose state derives from a DI parameter via initialization logic (e.g. a `Meter` that produces `Counter<T>` fields) may keep an explicit constructor
- **Static endpoint classes** with static `Handle` methods
- **`OneOf<>`** for feature results — not exceptions for expected errors
- **`EnsureThat`** for domain invariants in entities
- Expression-bodied members where concise
- No `this.` qualifier
- Implicit access modifiers where possible
- Interface + implementation in the same file for DB commands/queries
- **Never use MediatR for commands/queries** — only for domain event dispatch via `IDomainEvent`
- **Never put business logic in `src/api/`** — it belongs in `src/service/`
