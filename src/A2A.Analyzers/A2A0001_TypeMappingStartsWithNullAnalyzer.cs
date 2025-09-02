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
        if (baseType.OriginalDefinition.ToDisplayString() != "A2A.BaseKindDiscriminatorConverter<TBase, TKind>")
            return;

        // Find override property TypeMapping
        foreach (var member in classDecl.Members)
        {
            if (member is PropertyDeclarationSyntax prop && prop.Identifier.Text == "TypeMapping")
            {
                // Only check overrides
                if (!prop.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)))
                    continue;

                // Expect an array initializer with first element null
                if (prop.ExpressionBody?.Expression is not null)
                {
                    if (prop.ExpressionBody.Expression is ImplicitArrayCreationExpressionSyntax implicitArray &&
                        implicitArray.Initializer is { } init)
                    {
                        if (init.Expressions.Count == 0) return; // handled by compiler
                        var first = init.Expressions[0];
                        if (first is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression))
                            return; // ok
                    }
                    else if (prop.ExpressionBody.Expression is ArrayCreationExpressionSyntax arrayCreation &&
                             arrayCreation.Initializer is { } init2)
                    {
                        if (init2.Expressions.Count == 0) return;
                        var first = init2.Expressions[0];
                        if (first is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression))
                            return; // ok
                    }
                }
                else if (prop.Initializer?.Value is { } valueExpr)
                {
                    if (valueExpr is ImplicitArrayCreationExpressionSyntax iac && iac.Initializer is { } init3)
                    {
                        if (init3.Expressions.Count == 0) return;
                        var first = init3.Expressions[0];
                        if (first is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression))
                            return; // ok
                    }
                }

                // If we get here, not starting with null
                var diag = Diagnostic.Create(Rule, prop.GetLocation(), classSymbol.Name);
                context.ReportDiagnostic(diag);
            }
        }
    }
}
