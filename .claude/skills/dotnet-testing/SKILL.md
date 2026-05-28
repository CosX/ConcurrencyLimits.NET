---
name: dotnet-testing
description: Unit testing patterns and conventions for this project. Use when writing or reviewing unit tests.
---

## Testing Stack

- **xUnit** — test framework, use `[Fact]` for all tests
- **FakeItEasy** — mocking: `A.Fake<IInterface>()`, `A.CallTo(() => ...).Returns(...)`, `.MustHaveHappenedOnceExactly()`
- **Shouldly** — assertions: `.ShouldBe()`, `.ShouldBeOfType<T>()`, `.ShouldBeTrue()`

## Conventions

- Tests mirror source structure under `tests/UnitTests/`
- Use existing `*TestDataBuilder` classes for test data — search the codebase before creating new ones
- Feature tests: instantiate the feature directly with fakes, call `Execute`, assert on the `OneOf` result

## Example: Feature Test

```csharp
public class MyFeatureTests
{
    readonly IGetMyEntity _getEntity = A.Fake<IGetMyEntity>();
    readonly IUpdateMyEntity _updateEntity = A.Fake<IUpdateMyEntity>();
    readonly MyFeature _feature;

    public MyFeatureTests() => _feature = new MyFeature(_getEntity, _updateEntity);

    [Fact]
    public async Task Execute_HappyPath_ShouldReturnResult()
    {
        var entity = MyEntityTestDataBuilder.NewDefault();
        A.CallTo(() => _getEntity.ById("ABC")).Returns(entity);

        var result = await _feature.Execute(new MyFeature.Command(entity, "Test", 100));

        result.Value.ShouldBeOfType<MyFeature.Result>();
        A.CallTo(() => _updateEntity.Execute(A<MyEntity>._)).MustHaveHappenedOnceExactly();
    }
}
```
