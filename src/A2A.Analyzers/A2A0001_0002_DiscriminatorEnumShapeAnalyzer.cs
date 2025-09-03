using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;

namespace A2A.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class A2A0001_0002_DiscriminatorEnumShapeAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor UnknownRule = new(
        id: "A2A0001",
        title: "Discriminator enums must start with Unknown = 0",
        messageFormat: "Enum '{0}' used as discriminator must have 'Unknown = 0' as the first member",
        category: "A2A",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor CountRule = new(
        id: "A2A0002",
        title: "Discriminator enums must end with Count",
        messageFormat: "Enum '{0}' used as discriminator must end with 'Count' sentinel",
        category: "A2A",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [UnknownRule, CountRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            // Resolve the generic converter type once per compilation
            var converterGeneric = startContext.Compilation.GetTypeByMetadataName("A2A.BaseKindDiscriminatorConverter`2");
            if (converterGeneric is null)
            {
                return;
            }

            // Use syntax-based analysis for enum declarations to provide local diagnostics for code fixes
            startContext.RegisterSyntaxNodeAction(syntaxContext =>
            {
                var enumDecl = (EnumDeclarationSyntax)syntaxContext.Node;
                var enumSymbol = syntaxContext.SemanticModel.GetDeclaredSymbol(enumDecl);
                if (enumSymbol is null)
                {
                    return;
                }

                // Check if this enum is used as a discriminator type
                var isDiscriminatorEnum = IsDiscriminatorEnum(syntaxContext.SemanticModel.Compilation, enumSymbol, converterGeneric);
                if (!isDiscriminatorEnum)
                {
                    return;
                }

                // Validate and report diagnostics directly on the enum declaration
                ValidateEnumShapeWithSyntaxContext(syntaxContext, enumSymbol, enumDecl);
            }, SyntaxKind.EnumDeclaration);
        });
    }

    private static bool IsDiscriminatorEnum(Compilation compilation, INamedTypeSymbol enumSymbol, INamedTypeSymbol converterGeneric)
    {
        // Look for any class in the compilation that uses this enum as a discriminator
        foreach (var type in compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type).OfType<INamedTypeSymbol>())
        {
            if (type.TypeKind != TypeKind.Class)
                continue;

            var baseType = type.BaseType as INamedTypeSymbol;
            if (baseType is null)
                continue;

            if (!SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, converterGeneric))
                continue;

            if (baseType.TypeArguments.Length != 2)
                continue;

            if (SymbolEqualityComparer.Default.Equals(baseType.TypeArguments[1], enumSymbol))
                return true;
        }

        return false;
    }

    private static void ValidateEnumShapeWithSyntaxContext(SyntaxNodeAnalysisContext context, INamedTypeSymbol enumSymbol, EnumDeclarationSyntax enumDecl)
    {
        var underlying = enumSymbol.EnumUnderlyingType;
        if (underlying is null)
        {
            return;
        }

        bool isUnsigned = underlying.SpecialType is SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64;

        bool hasAny = false;
        string? firstName = null;
        string? lastName = null;
        bool minSet = false, maxSet = false;
        ulong min = 0, max = 0;

        // Local comparison helpers avoid allocations and branch on signedness only in comparisons
        bool Less(ulong a, ulong b) => isUnsigned ? a < b : unchecked((long)a) < unchecked((long)b);
        bool Greater(ulong a, ulong b) => isUnsigned ? a > b : unchecked((long)a) > unchecked((long)b);

        foreach (var member in enumSymbol.GetMembers())
        {
            if (member is not IFieldSymbol field || !field.HasConstantValue)
            {
                continue;
            }

            hasAny = true;

            // Read constant as widened 64-bit value without boxing or Convert
            ulong value;
            switch (underlying.SpecialType)
            {
                case SpecialType.System_Byte:
                    value = (byte)field.ConstantValue!;
                    break;
                case SpecialType.System_UInt16:
                    value = (ushort)field.ConstantValue!;
                    break;
                case SpecialType.System_UInt32:
                    value = (uint)field.ConstantValue!;
                    break;
                case SpecialType.System_UInt64:
                    value = (ulong)field.ConstantValue!;
                    break;
                case SpecialType.System_SByte:
                    value = unchecked((ulong)(long)(sbyte)field.ConstantValue!);
                    break;
                case SpecialType.System_Int16:
                    value = unchecked((ulong)(long)(short)field.ConstantValue!);
                    break;
                case SpecialType.System_Int32:
                    value = unchecked((ulong)(long)(int)field.ConstantValue!);
                    break;
                case SpecialType.System_Int64:
                    value = unchecked((ulong)(long)field.ConstantValue!);
                    break;
                default:
                    continue; // Unknown underlying type; ignore
            }

            if (!minSet || Less(value, min))
            {
                minSet = true;
                min = value;
                firstName = field.Name;
            }
            // For max, if equal we pick the later-declared member
            if (!maxSet || Greater(value, max) || value == max)
            {
                maxSet = true;
                max = value;
                lastName = field.Name;
            }
        }

        if (!hasAny)
        {
            return;
        }

        bool unknownFail = !(firstName == "Unknown" && (isUnsigned ? min == 0UL : unchecked((long)min) == 0L));
        bool countFail = lastName != "Count";

        if (unknownFail || countFail)
        {
            // Use the enum identifier location for syntax-based analysis
            var enumNameLocation = enumDecl.Identifier.GetLocation();

            if (unknownFail)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnknownRule, enumNameLocation, enumSymbol.Name));
            }
            if (countFail)
            {
                context.ReportDiagnostic(Diagnostic.Create(CountRule, enumNameLocation, enumSymbol.Name));
            }
        }
    }
}
