# A2A .NET SDK Copilot Instructions

This file contains coding guidelines and requirements for maintaining the A2A .NET SDK. These instructions help ensure consistency and correctness when implementing features, especially discriminator-based serialization.

## Code Style Requirements

Follow all code style preferences defined in the `.editorconfig` of the repository at all times. Your code should not differ from the style elsewhere in the project.

If editor config doesn't specify a style or preference, assume the style as defined in the `.editorconfig` of the .NET runtime project here: https://github.com/dotnet/runtime/blob/main/.editorconfig

In event of a conflict between the two, local (this repo) `.editorconfig` wins.

## Discriminator Converter Implementation Requirements

### Overview
The A2A SDK uses a custom JSON discriminator pattern for polymorphic serialization of types like `A2AEvent`, `Part`, and `FileContent`. This approach allows for specific error handling with `A2AException` and error codes instead of the default `NotSupportedException` from System.Text.Json.

### String Constant Patterns

When implementing discriminator-based serialization:

1. **Use internal static classes for kind constants** instead of enums
   - Create static classes like `A2AEventKind`, `PartKind`, `FileContentKind`
   - Use `const string` fields with lowercase kebab-case values
   - Include XML documentation linking to related types

   ```csharp
   public static class PartKind
   {
       /// <summary>
       /// A text part containing plain textual content.
       /// </summary>
       /// <seealso cref="TextPart"/>
       public const string Text = "text";
   }
   ```

2. **Use BaseKindDiscriminatorConverter**
   - Inherit from `BaseKindDiscriminatorConverter<TBase>`
   - Implement `KindToTypeMapping` as `IReadOnlyDictionary<string, Type>`
   - Implement `DisplayName` property for error messages

   ```csharp
   internal class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<T> where T : Part
   {
       protected override IReadOnlyDictionary<string, Type> KindToTypeMapping { get; } = new Dictionary<string, Type>
       {
           [PartKind.Text] = typeof(TextPart),
           [PartKind.File] = typeof(FilePart),
           [PartKind.Data] = typeof(DataPart)
       };

       protected override string DisplayName { get; } = "part";
   }
   ```

3. **Primary constructor usage for derived types**
   - Use primary constructors in derived classes
   - Pass the appropriate string constant to the base constructor

   ```csharp
   public sealed class TextPart() : Part(PartKind.Text)
   {
       // Implementation
   }
   ```

### Error Handling Requirements

1. **Use specific error messages**
   - Missing discriminator: "Missing required 'kind' discriminator for {TypeName}"
   - Invalid discriminator type: "Invalid 'kind' discriminator for {TypeName}: '{value}'"
   - Unknown kind value: "Unknown {displayName} kind: '{kindValue}'"
   - Deserialization failure: "Failed to deserialize '{kindValue}' {displayName}"

2. **Use appropriate A2AErrorCode**
   - All discriminator-related errors should use `A2AErrorCode.InvalidRequest`

### Maintenance Guidelines

#### When Adding New Discriminated Types

1. **Create the kind constants class**
   - Use static class with const string fields
   - Follow kebab-case naming convention
   - Add comprehensive XML documentation

2. **Create the base class**
   - Use primary constructor accepting string kind parameter
   - Add JsonConverter attribute pointing to your converter
   - Include JsonDerivedType attributes for all derived types
   - Add discriminator property with proper JsonPropertyName and JsonPropertyOrder

3. **Create the converter**
   - Inherit from `BaseKindDiscriminatorConverter<TBase>`
   - Implement required abstract members
   - Use dictionary mapping for kind-to-type relationships

4. **Create derived classes**
   - Use primary constructors
   - Pass appropriate kind constant to base constructor

#### When Adding New Derived Types

1. **Add new kind constant** to the appropriate kind constants class
2. **Add mapping entry** in the converter's KindToTypeMapping
3. **Add JsonDerivedType attribute** to the base class
4. **Implement the derived class** with primary constructor
5. **Add comprehensive tests** for serialization/deserialization

### JSON Serialization Guidelines

1. **Property naming**: Use camelCase for JSON property names
2. **Property ordering**: Discriminator property should have `JsonPropertyOrder(int.MinValue)`
3. **Required properties**: Mark discriminators with `JsonRequired`
4. **Converter placement**: Use `JsonConverter` attribute on base classes, not derived classes

### Testing Requirements

When implementing discriminator converters, ensure:

1. **Successful serialization** of all derived types
2. **Successful deserialization** with valid kind values
3. **Proper error handling** for missing/invalid/unknown kind values
4. **Round-trip compatibility** (serialize then deserialize equals original)
5. **Backward compatibility** with existing JSON payloads

## Performance Considerations

1. **Use readonly dictionaries** for kind-to-type mappings
2. **Avoid repeated allocations** in converter hot paths
3. **Cache type info** when possible
4. **Use culture-invariant operations** for string handling

## Implemention guidelines

1. Follow **principle of least privilege/visibility** for new types

## Example Implementation

See the existing implementations of `A2AEvent`, `Part`, and `FileContent` hierarchies as reference patterns for implementing new discriminated types.