using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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
        // Only analyze classes that derive from BaseKindDiscriminatorConverter<,>
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        var classSymbol = model.GetDeclaredSymbol(classDecl);
        if (classSymbol?.BaseType is null)
            return;

        if (classSymbol.BaseType.OriginalDefinition?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::A2A.BaseKindDiscriminatorConverter<TBase, TKind>")
            return;

        // TKind symbol
        if (classSymbol.BaseType is INamedTypeSymbol baseNamed && baseNamed.TypeArguments.Length == 2)
        {
            if (baseNamed.TypeArguments[1] is not INamedTypeSymbol tKind || tKind.TypeKind != TypeKind.Enum)
                return;

            // Validate the enum shape because it's actually used as a discriminator type parameter.
            ValidateEnumShape(context, tKind);
        }
    }

    private static void ValidateEnumShape(SyntaxNodeAnalysisContext context, INamedTypeSymbol enumSymbol)
    {
        // Prefer to report on the enum identifier for better UX (code fix on enum name)
        var enumDecl = enumSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken) as EnumDeclarationSyntax;
        var enumNameLocation = enumDecl?.Identifier.GetLocation() ?? enumSymbol.Locations.FirstOrDefault();

        // Get members (fields)
        var members = enumSymbol.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).OrderBy(f => f.ConstantValue).ToList();
        if (members.Count == 0) return;

        // Check Unknown = 0 is first
        var first = members[0];
        if (!(first.Name == "Unknown" && Convert.ToInt64(first.ConstantValue, System.Globalization.CultureInfo.InvariantCulture) == 0))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnknownRule, enumNameLocation, enumSymbol.Name));
        }

        // Check last is Count
        var last = members.Last();
        if (last.Name != "Count")
        {
            context.ReportDiagnostic(Diagnostic.Create(CountRule, enumNameLocation, enumSymbol.Name));
        }
    }
}
