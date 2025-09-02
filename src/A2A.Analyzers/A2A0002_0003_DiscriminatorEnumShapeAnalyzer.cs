using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace A2A.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class A2A0002_0003_DiscriminatorEnumShapeAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor UnknownRule = new(
        id: "A2A0002",
        title: "Discriminator enums must start with Unknown = 0",
        messageFormat: "Enum '{0}' used as discriminator must have 'Unknown = 0' as the first member",
        category: "A2A",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor CountRule = new(
        id: "A2A0003",
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
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeEnum, SyntaxKind.EnumDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        var classSymbol = model.GetDeclaredSymbol(classDecl);
        if (classSymbol?.BaseType is null)
            return;

        if (classSymbol.BaseType.OriginalDefinition?.ToDisplayString() != "A2A.BaseKindDiscriminatorConverter<TBase, TKind>")
            return;

        // TKind symbol
        if (classSymbol.BaseType is INamedTypeSymbol baseNamed && baseNamed.TypeArguments.Length == 2)
        {
            var tKind = baseNamed.TypeArguments[1] as INamedTypeSymbol;
            if (tKind is null || tKind.TypeKind != TypeKind.Enum)
                return;

            // require [JsonConverter] on the enum
            var hasJsonConverter = tKind.GetAttributes().Any(a => a.AttributeClass?.Name.Contains("JsonConverter") == true);
            if (!hasJsonConverter)
                return;

            ValidateEnumShape(context, tKind);
        }
    }

    private static void AnalyzeEnum(SyntaxNodeAnalysisContext context)
    {
        // If an enum is decorated with [JsonConverter], proactively validate too
        var enumDecl = (EnumDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        var symbol = model.GetDeclaredSymbol(enumDecl) as INamedTypeSymbol;
        if (symbol is null) return;

        var hasJsonConverter = symbol.GetAttributes().Any(a => a.AttributeClass?.Name.Contains("JsonConverter") == true);
        if (!hasJsonConverter)
            return;

        ValidateEnumShape(context, symbol);
    }

    private static void ValidateEnumShape(SyntaxNodeAnalysisContext context, INamedTypeSymbol enumSymbol)
    {
        // Get members (fields)
        var members = enumSymbol.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).OrderBy(f => f.ConstantValue).ToList();
        if (members.Count == 0) return;

        // Check Unknown = 0 is first
        var first = members.First();
        if (!(first.Name == "Unknown" && Convert.ToInt64(first.ConstantValue, System.Globalization.CultureInfo.DefaultThreadCurrentCulture) == 0))
        {
            var loc = first.Locations.FirstOrDefault() ?? enumSymbol.Locations.First();
            context.ReportDiagnostic(Diagnostic.Create(UnknownRule, loc, enumSymbol.Name));
        }

        // Check last is Count
        var last = members.Last();
        if (last.Name != "Count")
        {
            var loc = last.Locations.FirstOrDefault() ?? enumSymbol.Locations.First();
            context.ReportDiagnostic(Diagnostic.Create(CountRule, loc, enumSymbol.Name));
        }
    }
}
