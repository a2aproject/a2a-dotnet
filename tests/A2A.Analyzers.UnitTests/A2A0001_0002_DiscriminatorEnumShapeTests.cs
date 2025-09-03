using Microsoft.CodeAnalysis;
using System.Reflection;
using Xunit;
using VerifyAnalyzer = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<A2A.Analyzers.A2A0001_0002_DiscriminatorEnumShapeAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCountFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    A2A.Analyzers.A2A0001_0002_DiscriminatorEnumShapeAnalyzer,
    A2A.Analyzers.A2A0002_CountLastCodeFix,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
// Concrete test types so we can add additional references
using VerifyUnknownFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    A2A.Analyzers.A2A0001_0002_DiscriminatorEnumShapeAnalyzer,
    A2A.Analyzers.A2A0001_UnknownFirstCodeFix,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace A2A.Analyzers.UnitTests;

public class A2A0001_0002_DiscriminatorEnumShapeTests
{
    private const string UsingsAndNamespace =
        """
        using A2A;

        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using System.Text.Json.Serialization.Metadata;

        namespace A2A;

        """;
    [Fact]
    public async Task ReportsOnEnumIdentifier_And_FixesUnknownFirstAsync()
    {
        // Include Count so only A2A0001 is reported (Unknown missing)
        var test =
            """
            internal enum {|#0:TestPartKind|}
            {
                Text, File, Data, Count
            }

            internal abstract class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<Part, TestPartKind> where T : Part
            {
                protected override string DisplayName { get; } = "part";
                protected override TestPartKind UnknownValue { get; } = default;
            }
            """;

        var expected =
            """
            internal enum TestPartKind
            {
                /// <summary> Unknown value, used for unrecognized values. </summary>
                Unknown = 0,
                Text, File, Data, Count
            }

            internal abstract class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<Part, TestPartKind> where T : Part
            {
                protected override string DisplayName { get; } = "part";
                protected override TestPartKind UnknownValue { get; } = default;
            }
            """;
        var testInstance = new VerifyUnknownFixTest { TestCode = UsingsAndNamespace + test, FixedCode = UsingsAndNamespace + expected, CodeFixTestBehaviors = Microsoft.CodeAnalysis.Testing.CodeFixTestBehaviors.SkipLocalDiagnosticCheck }.AsStrongNamedAssembly();
        testInstance.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).GetTypeInfo().Assembly.Location));
        testInstance.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(A2A.BaseKindDiscriminatorConverter<,>).GetTypeInfo().Assembly.Location));

        var expectedDiag = VerifyAnalyzer.Diagnostic("A2A0001").WithLocation(0).WithArguments("TestPartKind");
        testInstance.ExpectedDiagnostics.Add(expectedDiag);

        await testInstance.RunAsync();
    }

    [Fact]
    public async Task FixesCountLastAsync()
    {
        var test =
            """
            internal enum {|#0:TestPartKind|}
            {
                Unknown = 0, Text, File, Data
            }

            internal abstract class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<Part, TestPartKind> where T : Part
            {
                protected override string DisplayName { get; } = "part";
                protected override TestPartKind UnknownValue { get; } = default;
            }
            """;

        var expected =
            """
            internal enum TestPartKind
            {
                Unknown = 0, Text, File, Data,
                /// <summary> Helper value to track the number of enum values when used as array indices. This must always be the last value in the enumeration. </summary>
                Count
            }

            internal abstract class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<Part, TestPartKind> where T : Part
            {
                protected override string DisplayName { get; } = "part";
                protected override TestPartKind UnknownValue { get; } = default;
            }
            """;
        var testInstance = new VerifyCountFixTest { TestCode = UsingsAndNamespace + test, FixedCode = UsingsAndNamespace + expected, CodeFixTestBehaviors = Microsoft.CodeAnalysis.Testing.CodeFixTestBehaviors.SkipLocalDiagnosticCheck }.AsStrongNamedAssembly();
        testInstance.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).GetTypeInfo().Assembly.Location));
        testInstance.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(A2A.BaseKindDiscriminatorConverter<,>).GetTypeInfo().Assembly.Location));

        var expectedDiag = VerifyAnalyzer.Diagnostic("A2A0002").WithLocation(0).WithArguments("TestPartKind");
        testInstance.ExpectedDiagnostics.Add(expectedDiag);

        await testInstance.RunAsync();
    }
}
