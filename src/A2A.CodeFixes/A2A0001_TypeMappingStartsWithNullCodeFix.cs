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
    public override ImmutableArray<string> FixableDiagnosticIds => ["A2A0001"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
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
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);

        // Find initializer across array and collection expressions
        InitializerExpressionSyntax? arrayInit = null;
        CollectionExpressionSyntax? collectionInit = null;

        SyntaxNode? exprOrValue = prop.ExpressionBody?.Expression ?? prop.Initializer?.Value;
        switch (exprOrValue)
        {
            case ImplicitArrayCreationExpressionSyntax iac:
                arrayInit = iac.Initializer;
                break;
            case ArrayCreationExpressionSyntax ac:
                arrayInit = ac.Initializer;
                break;
            case CollectionExpressionSyntax ce:
                collectionInit = ce;
                break;
        }

        if (arrayInit is null && collectionInit is null)
        {
            return document; // nothing to do safely
        }

        if (collectionInit is not null)
        {
            var newElements = collectionInit.Elements.Insert(0, SyntaxFactory.ExpressionElement(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
            var newCollection = collectionInit.WithElements(newElements);

            if (prop.ExpressionBody?.Expression is CollectionExpressionSyntax)
            {
                var newProp = prop.WithExpressionBody(prop.ExpressionBody!.WithExpression(newCollection));
                editor.ReplaceNode(prop, newProp);
            }
            else if (prop.Initializer?.Value is CollectionExpressionSyntax)
            {
                var newProp = prop.WithInitializer(prop.Initializer!.WithValue(newCollection));
                editor.ReplaceNode(prop, newProp);
            }
            return editor.GetChangedDocument();
        }

        // Array initializers path
        var newInit = arrayInit!.WithExpressions(arrayInit.Expressions.Insert(0, SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));

        SyntaxNode newPropNode = prop;
        if (prop.ExpressionBody?.Expression is ImplicitArrayCreationExpressionSyntax iac3)
        {
            newPropNode = prop.WithExpressionBody(prop.ExpressionBody.WithExpression(iac3.WithInitializer(newInit)));
        }
        else if (prop.ExpressionBody?.Expression is ArrayCreationExpressionSyntax ac3)
        {
            newPropNode = prop.WithExpressionBody(prop.ExpressionBody.WithExpression(ac3.WithInitializer(newInit)));
        }
        else if (prop.Initializer?.Value is ImplicitArrayCreationExpressionSyntax iac4)
        {
            newPropNode = prop.WithInitializer(prop.Initializer.WithValue(iac4.WithInitializer(newInit)));
        }
        else if (prop.Initializer?.Value is ArrayCreationExpressionSyntax ac4)
        {
            newPropNode = prop.WithInitializer(prop.Initializer.WithValue(ac4.WithInitializer(newInit)));
        }

        editor.ReplaceNode(prop, newPropNode);
        return editor.GetChangedDocument();
    }
}
