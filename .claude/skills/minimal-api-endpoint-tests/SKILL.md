---
name: minimal-api-endpoint-tests
description: Use when writing or adding API tests for a Minimal API endpoint in an ASP.NET Core project. Covers test class structure, scenario setup, claims, and assertions using Alba/WebApplicationFactory.
---

# Write Tests for a Minimal API Endpoint

## Prerequisites

Before writing tests, find existing test infrastructure:

1. **Find a base test class** — look for `TestContext` or similar that wraps `Host.Scenario()`.
2. **Find a test fixture** — look for `TestFixture` or `WebApplicationFactory` setup.
3. **Find existing test examples** — match the existing assertion style (Shouldly, FluentAssertions, etc.).

Match the existing conventions exactly. Do NOT invent new patterns.

## Test Structure

Create a test class in the test project under `Features/{Area}/`:

```csharp
public class {Verb}{Entity}EndpointTests : TestContext
{
    public {Verb}{Entity}EndpointTests(TestFixture testFixture) : base(testFixture)
    {
        testFixture.ClearRecordedCalls();
        Scenario = Host.Scenario(_ =>
        {
            _.WithClaim(new Claim("scope", "{required-scope}"));
            _.Post.Json(new RequestDto { ... }).ToUrl("/api/route");
            // or: _.Get.Url("/api/route");
        });
    }

    [Fact]
    public async Task Should_return_ok()
    {
        await RunScenario();
        StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_return_expected_data()
    {
        await RunScenario();
        var response = Result.ReadAsJson<ResponseDto>();
        response.ShouldSatisfyAllConditions(
            r => r.Property.ShouldBe("expected")
        );
    }
}
```

## Naming Conventions

| Type | Pattern | Example |
|------|---------|---------|
| Test class | `{Verb}{Entity}EndpointTests` | `CreateOrderEndpointTests` |
