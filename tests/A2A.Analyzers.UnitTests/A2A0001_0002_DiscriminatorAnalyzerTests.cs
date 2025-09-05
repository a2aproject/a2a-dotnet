using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;
using Xunit;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<A2A.Analyzers.A2A0001_0002_DiscriminatorEnumShapeAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyAnalyzer = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<A2A.Analyzers.A2A0001_0002_DiscriminatorEnumShapeAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

#if NET8_0
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

using System.Globalization;

using PackageIdentity = NuGet.Packaging.Core.PackageIdentity;
#endif

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
                protected override JsonTypeInfo<TestPartKind> JsonTypeInfo { get; } = (JsonTypeInfo<TestPartKind>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TestPartKind));
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
                protected override JsonTypeInfo<TestPartKind> JsonTypeInfo { get; } = (JsonTypeInfo<TestPartKind>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TestPartKind));
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
                protected override JsonTypeInfo<TestPartKind> JsonTypeInfo { get; } = (JsonTypeInfo<TestPartKind>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TestPartKind));
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
                protected override JsonTypeInfo<TestPartKind> JsonTypeInfo { get; } = (JsonTypeInfo<TestPartKind>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(TestPartKind));
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
            project = project
                .WithAssemblyName(asmName)
                .WithCompilationOptions(project.CompilationOptions!
                    .WithCryptoPublicKey(publicKeyBytes)
                    .WithPublicSign(true));
#if NET8_0
            project = replaceSystemTextJsonDllsWithNet9NugetPackageContents(ref project);
            // The Roslyn Workspace doesn't properly *replace* STJ DLLs with nupkg contents if we use AddPackage
            // (as we do for netstandard2.0) so we have to manually get the entire dependency tree of STJ9 and
            // replace the corresponding DLLs from 8.0 with those from the pkg otherwise we'll get a dependency
            // mismatch exception at runtime ("Type Exists in both 8.0 and 9.0", etc.)
            // https://github.com/dotnet/roslyn-sdk/issues/1219
            static Project replaceSystemTextJsonDllsWithNet9NugetPackageContents(ref Project p)
            {
                NuGetFramework targetFramework = NuGetFramework.ParseFolder("net8.0");
                var nugetSettings = Settings.LoadDefaultSettings(null);
                var stj9dlls = getAllDllPaths(
                        resolveAllDependenciesAsync(new("System.Text.Json", new NuGet.Versioning.NuGetVersion(9, 0, 8)),
                            nugetSettings, targetFramework).GetAwaiter().GetResult(),
                        nugetSettings, targetFramework);

                return p.WithMetadataReferences(p.MetadataReferences.Where(i => !stj9dlls.Any(j => Path.GetFileName(j).Equals(Path.GetFileName(i.Display), StringComparison.OrdinalIgnoreCase)))
                    .Concat(stj9dlls.Select(i => MetadataReference.CreateFromFile(i))));

                static IEnumerable<string> getAllDllPaths(IEnumerable<PackageIdentity> packages, ISettings settings, NuGetFramework targetFramework)
                {
                    var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);
                    var allDlls = new List<string>();

                    foreach (var package in packages)
                    {
                        string packagePath = Path.Combine(globalPackagesFolder, package.Id.ToLower(CultureInfo.InvariantCulture), package.Version.ToNormalizedString());
                        // You might want to try several fallback folder names for TFM
                        string libPath = Path.Combine(packagePath, "lib", targetFramework.GetShortFolderName());

                        if (Directory.Exists(libPath))
                        {
                            foreach (var dll in Directory.GetFiles(libPath, "*.dll"))
                            {
                                yield return dll;
                            }
                        }
                    }
                }

                static async Task<IEnumerable<PackageIdentity>> resolveAllDependenciesAsync(PackageIdentity rootPackage, ISettings settings, NuGetFramework targetFramework)
                {
                    var resolved = new HashSet<PackageIdentity>(PackageIdentityComparer.Default);
                    var toProcess = new Queue<PackageIdentity>([rootPackage]);

                    // Load the active nuget.config settings
                    var packageSourceProvider = new PackageSourceProvider(settings);
                    var packageSources = packageSourceProvider.LoadPackageSources().Where(s => s.IsEnabled).ToList();

                    var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);
                    while (toProcess.Count > 0)
                    {
                        var current = toProcess.Dequeue();
                        if (resolved.Contains(current)) continue;

                        resolved.Add(current);

                        string packagePath = await findAndDownloadPackageAsync(current, globalPackagesFolder, packageSources);

                        // Get dependencies
                        foreach (var dep in getPackageDependencies(packagePath, targetFramework))
                        {
                            if (!resolved.Contains(dep))
                                toProcess.Enqueue(dep);
                        }
                    }

                    return resolved;

                    static async Task<string> findAndDownloadPackageAsync(PackageIdentity package, string globalPackagesFolder, IEnumerable<PackageSource> packageSources)
                    {
                        // First, check if package exists locally in global packages folder
                        string localPackagePath = Path.Combine(globalPackagesFolder, package.Id.ToLower(CultureInfo.InvariantCulture), package.Version.ToNormalizedString());
                        string localNupkgPath = Path.Combine(localPackagePath, $"{package.Id}.{package.Version.ToNormalizedString()}.nupkg");

                        if (File.Exists(localNupkgPath))
                        {
                            return localNupkgPath;
                        }

                        string downloadFolder = Path.Combine(Environment.CurrentDirectory, "packages");
                        string downloadPath = Path.Combine(downloadFolder, $"{package.Id}.{package.Version}.nupkg");

                        if (File.Exists(downloadPath))
                        {
                            return downloadPath;
                        }

                        // If not found locally, search through package sources in order
                        foreach (var packageSource in packageSources)
                        {
                            try
                            {
                                var sourceRepository = Repository.Factory.GetCoreV3(packageSource);
                                var findPackageByIdResource = await sourceRepository.GetResourceAsync<FindPackageByIdResource>();

                                // Check if package exists in this source
                                SourceCacheContext cache = new();
                                if (await findPackageByIdResource.DoesPackageExistAsync(package.Id, package.Version, cache, NullLogger.Instance, CancellationToken.None))
                                {
                                    // Package found in this source, download it
                                    Directory.CreateDirectory(downloadFolder);

                                    using var packageDownloader = await findPackageByIdResource.GetPackageDownloaderAsync(package, cache, NullLogger.Instance, CancellationToken.None);
                                    await packageDownloader.CopyNupkgFileToAsync(downloadPath, CancellationToken.None);

                                    return downloadPath;
                                }
                            }
                            catch
                            {
                                // Continue to next source if this one fails
                                continue;
                            }
                        }

                        throw new InvalidOperationException($"Package {package.Id} {package.Version} not found in any configured package source.");
                    }

                    static IEnumerable<PackageIdentity> getPackageDependencies(string nupkgFile, NuGetFramework targetFramework)
                    {
                        if (!File.Exists(nupkgFile))
                            throw new FileNotFoundException("Package file missing.", nupkgFile);

                        using var packageReader = new PackageArchiveReader(nupkgFile);
                        var nuspecReader = packageReader.NuspecReader;
                        var dependenciesGroups = nuspecReader.GetDependencyGroups();

                        // Find dependencies for target framework or fallback
                        var depsForFramework = dependenciesGroups.FirstOrDefault(g => g.TargetFramework.Equals(targetFramework))
                            ?? dependenciesGroups.FirstOrDefault(g => g.TargetFramework.Equals(NuGetFramework.AnyFramework));

                        foreach (var dep in depsForFramework?.Packages ?? [])
                        {
                            yield return new PackageIdentity(dep.Id, dep.VersionRange.MinVersion);
                            // Note: VersionRange may specify min and max, 
                            // here we take min version for simplicity
                        }
                    }
                }
            }
#endif
            return project.Solution;
        });

#if NET8_0
        test.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
#elif NET9_0_OR_GREATER
        test.ReferenceAssemblies = ReferenceAssemblies.Net.Net90;
#else
        test.ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20
            .AddPackages([new("System.Text.Json", "9.0.8")]);
#endif

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(BaseKindDiscriminatorConverter<,>).Assembly.Location));

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