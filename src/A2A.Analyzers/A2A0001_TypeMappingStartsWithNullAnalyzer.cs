using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace A2A.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class A2A0001_TypeMappingStartsWithNullAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        id: "A2A0001",
        title: "TypeMapping must start with null",
        messageFormat: "Type '{0}' overrides TypeMapping but the first element is not null",
        category: "A2A",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Any type inheriting from BaseKindDiscriminatorConverter must have null as the first element of its TypeMapping to reserve index 0 for Unknown.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        var classSymbol = model.GetDeclaredSymbol(classDecl);
        if (classSymbol is null)
            return;

        // Check base type is BaseKindDiscriminatorConverter<,>
        var baseType = classSymbol.BaseType;
        if (baseType is null || baseType.OriginalDefinition is null)
            return;
        if (baseType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::A2A.BaseKindDiscriminatorConverter<TBase, TKind>")
            return;

        // Find override property TypeMapping
        foreach (var member in classDecl.Members)
        {
            if (member is PropertyDeclarationSyntax prop && prop.Identifier.Text == "TypeMapping")
            {
                // Only check overrides
                if (!prop.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)))
                    continue;

                // Examine array or collection initializers, both expression-bodied and initializer forms
                var exprOrValue = (SyntaxNode?)prop.ExpressionBody?.Expression ?? prop.Initializer?.Value;
                if (exprOrValue is null)
                    continue;

                // Handle C# collection expressions: [ ... ]
                if (exprOrValue is CollectionExpressionSyntax collection)
                {
                    if (!collection.Elements.Any())
                        return;

                    var firstEl = collection.Elements[0] as ExpressionElementSyntax;
                    if (firstEl?.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression))
                        return; // ok

                    Report(prop, classSymbol.Name, context);
                    continue;
                }

                // Handle implicit/explicit array creation expressions with initializers
                InitializerExpressionSyntax? init = null;
                if (exprOrValue is ImplicitArrayCreationExpressionSyntax iac && iac.Initializer is { } initIac)
                    init = initIac;
                else if (exprOrValue is ArrayCreationExpressionSyntax ac && ac.Initializer is { } initAc)
                    init = initAc;

                if (init is null)
                    continue;

                if (!init.Expressions.Any())
                    return;

                var first = init.Expressions[0];
                if (first is LiteralExpressionSyntax lit2 && lit2.IsKind(SyntaxKind.NullLiteralExpression))
                    return; // ok

                Report(prop, classSymbol.Name, context);
            }
        }
    }

    private static void Report(PropertyDeclarationSyntax prop, string className, SyntaxNodeAnalysisContext context)
    {
        var diag = Diagnostic.Create(Rule, prop.GetLocation(), className);
        context.ReportDiagnostic(diag);
    }
}
