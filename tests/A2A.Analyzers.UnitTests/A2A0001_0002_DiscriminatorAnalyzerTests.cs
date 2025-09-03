using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;
using System.Reflection;
using Xunit;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<A2A.Analyzers.A2A0001_0002_DiscriminatorEnumShapeAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyAnalyzer = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<A2A.Analyzers.A2A0001_0002_DiscriminatorEnumShapeAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace A2A.Analyzers.UnitTests;

public class A2A0001_0002_DiscriminatorAnalyzerTests
{
    private const string UsingsAndNamespace = """
        using A2A;

        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using System.Text.Json.Serialization.Metadata;

        namespace A2A;

        // Minimal type definitions needed for the analyzer tests
        public abstract class Part { }
        public class TextPart : Part { }
        public class FilePart : Part { }
        public class DataPart : Part { }

        public class DiscriminatorTypeMapping<T> where T : Enum
        {
            public DiscriminatorTypeMapping(params Type[] types) { }
        }

        public abstract class BaseKindDiscriminatorConverter<TBase, TKind> where TKind : Enum
        {
            protected abstract DiscriminatorTypeMapping<TKind> TypeMapping { get; }
            protected abstract string DisplayName { get; }
            protected abstract JsonTypeInfo<TKind> JsonTypeInfo { get; }
            protected abstract TKind UnknownValue { get; }
        }

        public static class A2AJsonUtilities
        {
            public static JsonSerializerOptions DefaultOptions { get; } = new JsonSerializerOptions();
        }

        """;

    [Fact]
    public async Task Reports_Both_A2A0001_and_A2A0002_When_UnknownMissing_And_CountMissing_OnEnumIdentifierAsync()
    {
        var test = """
            public enum {|#0:TestPartKind|}
            {
                Text,
                File,
                Data
            }

            internal class TestConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<Part, TestPartKind>
                where T : Part
            {
                protected override DiscriminatorTypeMapping<TestPartKind> TypeMapping { get; } = new(typeof(TextPart), typeof(FilePart), typeof(DataPart));
                protected override string DisplayName { get; } = "part";
                protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<TestPartKind> JsonTypeInfo { get; } = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<TestPartKind>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TestPartKind));
                protected override TestPartKind UnknownValue { get; } = default;
            }
            """;

        var expected1 = VerifyAnalyzer.Diagnostic("A2A0001").WithLocation(0).WithArguments("TestPartKind");
        var expected2 = VerifyAnalyzer.Diagnostic("A2A0002").WithLocation(0).WithArguments("TestPartKind");

        var testInstance = new AnalyzerTest { TestCode = UsingsAndNamespace + test }.ConfigureForTest();
        testInstance.ExpectedDiagnostics.Add(expected1);
        testInstance.ExpectedDiagnostics.Add(expected2);

        await testInstance.RunAsync();
    }

    [Fact]
    public async Task Reports_A2A0001_When_Unknown_Not_First_OnEnumIdentifierAsync()
    {
        var test = """
            public enum {|#0:TestPartKind|}
            {
                Text,
                Unknown = 0,
                File,
                Data,
                Count
            }

            internal class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<Part, TestPartKind>
                where T : Part
            {
                protected override DiscriminatorTypeMapping<TestPartKind> TypeMapping { get; } = new(typeof(TextPart), typeof(FilePart), typeof(DataPart));
                protected override string DisplayName { get; } = "part";
                protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<TestPartKind> JsonTypeInfo { get; } = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<TestPartKind>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TestPartKind));
                protected override TestPartKind UnknownValue { get; } = default;
            }
            """;

        var expected = VerifyAnalyzer.Diagnostic("A2A0001").WithLocation(0).WithArguments("TestPartKind");
        var testInstance = new AnalyzerTest { TestCode = UsingsAndNamespace + test }.ConfigureForTest();
        testInstance.ExpectedDiagnostics.Add(expected);
        await testInstance.RunAsync();
    }

    [Fact]
    public async Task Reports_A2A0002_When_Count_Missing_OnEnumIdentifierAsync()
    {
        var test = """
            public enum {|#0:TestPartKind|}
            {
                Unknown = 0,
                Text,
                File,
                Data
            }

            internal class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<Part, TestPartKind>
                where T : Part
            {
                protected override DiscriminatorTypeMapping<TestPartKind> TypeMapping { get; } = new(typeof(TextPart), typeof(FilePart), typeof(DataPart));
                protected override string DisplayName { get; } = "part";
                protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<TestPartKind> JsonTypeInfo { get; } = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<TestPartKind>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TestPartKind));
                protected override TestPartKind UnknownValue { get; } = default;
            }
            """;

        var expected = VerifyAnalyzer.Diagnostic("A2A0002").WithLocation(0).WithArguments("TestPartKind");
        var testInstance = new AnalyzerTest { TestCode = UsingsAndNamespace + test }.ConfigureForTest();
        testInstance.ExpectedDiagnostics.Add(expected);
        await testInstance.RunAsync();
    }

    [Fact]
    public async Task Reports_No_Diagnostics_When_Shape_Is_CorrectAsync()
    {
        var test = """
            public enum TestPartKind
            {
                Unknown = 0,
                Text,
                File,
                Data,
                Count
            }

            internal class PartConverterViaKindDiscriminator<T> : BaseKindDiscriminatorConverter<Part, TestPartKind>
                where T : Part
            {
                protected override DiscriminatorTypeMapping<TestPartKind> TypeMapping { get; } = new(typeof(TextPart), typeof(FilePart), typeof(DataPart));
                protected override string DisplayName { get; } = "part";
                protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<TestPartKind> JsonTypeInfo { get; } = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<TestPartKind>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TestPartKind));
                protected override TestPartKind UnknownValue { get; } = TestPartKind.Unknown;
            }
            """;

        var testInstance = new AnalyzerTest { TestCode = UsingsAndNamespace + test }.ConfigureForTest();
        await testInstance.RunAsync();
    }
}

internal static class TestExtensions
{
    private static readonly ImmutableArray<byte> PublicKey = [.. HexToBytes("0024000004800000940000000602000000240000525341310004000001000100fdff21bdfb01242cffd857fcfe4fd1048248b80c2d5779adf1916ba0f2fdfb7d9f780ba2c7eb359d8b2c40be090f54d99f29ada7769f3b50d5db1e92e645577abc702cb53a9ccdae1ff5aaf4c413f9ba4fd26f298f8756d38d0c4c9c813b39dd6f760f29ed0094f55af0dd698df03c714dace31a70362a2970fd0fa5a5dc5ec1")];
    public static AnalyzerTest<TVerifier> ConfigureForTest<TVerifier>(this AnalyzerTest<TVerifier> test, string asmName = "AnalyzerTests.Generated", string? publicKey = null)
        where TVerifier : IVerifier, new()
    {
        // Convert hex string to binary public key bytes
        var publicKeyBytes = string.IsNullOrWhiteSpace(publicKey) ? PublicKey : HexToBytes(publicKey);

        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var project = solution.GetProject(projectId)!;
            project = project.WithAssemblyName(asmName)
                .WithCompilationOptions(project.CompilationOptions!
                    .WithCryptoPublicKey(publicKeyBytes)
                    .WithPublicSign(true));
            return project.Solution;
        });

#if NET8_0
        test.TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
#elif NET9_0_OR_GREATER
        test.TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net90;
#else
        test.TestState.ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20;
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).GetTypeInfo().Assembly.Location));
#endif

        // Don't add the A2A assembly reference - the test will define the minimal types it needs

        return test;
    }

    // Helper: convert a hex string to a byte[] (two hex chars -> one byte)
    private static ImmutableArray<byte> HexToBytes(string? hex)
    {
        if (hex is null) throw new ArgumentNullException(nameof(hex));
        if ((hex.Length & 1) is not 0) throw new ArgumentException("Hex string must have an even length", nameof(hex));

        var bytes = ImmutableArray.CreateBuilder<byte>(hex.Length / 2);
        for (int i = 0; i < bytes.Capacity; i++)
        {
            bytes.Add(Convert.ToByte(hex.Substring(i * 2, 2), 16));
        }

        return bytes.DrainToImmutable();
    }
}