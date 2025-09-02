using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace A2A.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(A2A0003_CountLastCodeFix)), System.Composition.Shared]
internal sealed class A2A0003_CountLastCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["A2A0003"];

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
                title: "Ensure 'Count' is last",
                createChangedDocument: ct => EnsureCountLastAsync(context.Document, enumDecl, ct),
                equivalenceKey: "EnsureCountLast"),
            diagnostic);
    }

    private static async Task<Document> EnsureCountLastAsync(Document document, EnumDeclarationSyntax enumDecl, CancellationToken ct)
    {
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);
        var members = enumDecl.Members;

        // Remove existing Count if present
        var existingCount = members.FirstOrDefault(m => m.Identifier.Text == "Count");
        if (existingCount != default)
            editor.RemoveNode(existingCount);

        // Create Count (no value, compiler will assign next one)
        var count = SyntaxFactory.EnumMemberDeclaration("Count");

        // Insert at end
        editor.InsertAfter(members.Last(), count);
        return editor.GetChangedDocument();
    }
}
