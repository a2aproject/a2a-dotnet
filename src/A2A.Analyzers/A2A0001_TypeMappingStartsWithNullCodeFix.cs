using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace A2A.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(A2A0001_TypeMappingStartsWithNullCodeFix)), System.Composition.Shared]
internal sealed class A2A0001_TypeMappingStartsWithNullCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [A2A0001_TypeMappingStartsWithNullAnalyzer.Rule.Id];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        if (node is not PropertyDeclarationSyntax prop) return;

        context.RegisterCodeFix(
            Microsoft.CodeAnalysis.CodeActions.CodeAction.Create(
                title: "Insert null as first element",
                createChangedDocument: ct => InsertNullFirstAsync(context.Document, prop, ct),
                equivalenceKey: "InsertNullFirst"),
            diagnostic);
    }

    private static async Task<Document> InsertNullFirstAsync(Document document, PropertyDeclarationSyntax prop, CancellationToken ct)
    {
        var root = (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);

        // Handle expression-bodied property or initializer with array
        InitializerExpressionSyntax? init = null;
        if (prop.ExpressionBody?.Expression is ImplicitArrayCreationExpressionSyntax iac)
            init = iac.Initializer;
        else if (prop.ExpressionBody?.Expression is ArrayCreationExpressionSyntax ac)
            init = ac.Initializer;
        else if (prop.Initializer?.Value is ImplicitArrayCreationExpressionSyntax iac2)
            init = iac2.Initializer;
        else if (prop.Initializer?.Value is ArrayCreationExpressionSyntax ac2)
            init = ac2.Initializer;

        if (init is null)
            return document; // nothing to do safely

        var newInit = init.WithExpressions(init.Expressions.Insert(0, SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));

        SyntaxNode newProp = prop;
        if (prop.ExpressionBody?.Expression is ImplicitArrayCreationExpressionSyntax iac3)
        {
            newProp = prop.WithExpressionBody(prop.ExpressionBody.WithExpression(iac3.WithInitializer(newInit)));
        }
        else if (prop.ExpressionBody?.Expression is ArrayCreationExpressionSyntax ac3)
        {
            newProp = prop.WithExpressionBody(prop.ExpressionBody.WithExpression(ac3.WithInitializer(newInit)));
        }
        else if (prop.Initializer?.Value is ImplicitArrayCreationExpressionSyntax iac4)
        {
            newProp = prop.WithInitializer(prop.Initializer.WithValue(iac4.WithInitializer(newInit)));
        }
        else if (prop.Initializer?.Value is ArrayCreationExpressionSyntax ac4)
        {
            newProp = prop.WithInitializer(prop.Initializer.WithValue(ac4.WithInitializer(newInit)));
        }

        editor.ReplaceNode(prop, newProp);
        return editor.GetChangedDocument();
    }
}
