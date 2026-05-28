---
name: csharp-code-review-checklist
description: Use when reviewing C# code. Provides a structured checklist covering correctness, null safety, security, performance, SOLID, exception handling, edge cases, and testability.
---

# Code Review Checklist

Use this checklist when reviewing C# code. Report all findings — do not fix.

## Code Correctness
- Logic bugs, wrong operator usage, off-by-one errors
- Incorrect `async`/`await` usage (`.Result`, `.Wait()`, `async void`)
- Missing `await` on async calls
- Race conditions or shared mutable state

## Null Safety
- Missing null checks on inputs and return values
- Misuse of null-forgiving operator (`!`) without justification
- Gaps in nullable reference type annotations

## Security
- Raw SQL queries vulnerable to injection
- Logging of secrets, tokens, or PII
- Missing input validation at public API boundaries

## Performance
- N+1 query patterns
- Missing `AsNoTracking()` on read-only queries
- Missing `AsSplitQuery()` when including multiple collections
- Synchronous I/O blocking async code
- `ToList()` or enumeration inside loops
- Unnecessary allocations

## SOLID & Feature Pattern
- Single Responsibility violations (class doing too much)
- Feature calling another feature (load **feature-pattern** skill for details)
- Missing interface abstraction for testability
- Logic placed in constructors

## Exception Handling
- Swallowed exceptions (empty `catch` blocks)
- Overly broad `catch (Exception)` without re-throw or logging
- Exceptions used for control flow

## Edge Cases
- Empty collections not handled
- Zero, negative, or boundary values not validated
- Missing time zone awareness for date/time operations
- Unhandled enum values in `switch` expressions

## Testability
- `new` keyword used inside constructors for dependencies (hard to mock)
- Static state or singletons causing test interference
- Business logic hidden in private methods with no seam for testing
