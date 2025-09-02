using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace A2A.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(A2A0002_UnknownFirstCodeFix)), System.Composition.Shared]
internal sealed class A2A0002_UnknownFirstCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["A2A0002"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var enumDecl = node.FirstAncestorOrSelf<EnumDeclarationSyntax>();
        if (enumDecl is null) return;

        context.RegisterCodeFix(
            Microsoft.CodeAnalysis.CodeActions.CodeAction.Create(
                title: "Ensure 'Unknown = 0' is first",
                createChangedDocument: ct => EnsureUnknownFirstAsync(context.Document, enumDecl, ct),
                equivalenceKey: "EnsureUnknownFirst"),
            diagnostic);
    }

    private static async Task<Document> EnsureUnknownFirstAsync(Document document, EnumDeclarationSyntax enumDecl, CancellationToken ct)
    {
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);
        var members = enumDecl.Members;

        // Remove existing Unknown if present
        var existingUnknown = members.FirstOrDefault(m => m.Identifier.Text == "Unknown");
        if (existingUnknown != default)
            editor.RemoveNode(existingUnknown);

        // Create Unknown = 0
        var unknown = SyntaxFactory.EnumMemberDeclaration("Unknown")
            .WithEqualsValue(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))));

        // Insert at start
        editor.InsertBefore(members[0], unknown);
        return editor.GetChangedDocument();
    }
}
