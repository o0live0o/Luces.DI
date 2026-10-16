using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Luces.DI.Generators.Discovery;

/// <summary>
/// Fast filtering at the syntax level: only declarations that "might be registered" are worth entering semantic binding. Criteria: a class / record declaration, not abstract / static, and carrying a base list or attributes.
/// </summary>
internal static class CandidateSyntaxFilter
{
    public static bool IsCandidate(SyntaxNode node)
    {
        if (node is not TypeDeclarationSyntax declaration)
        {
            return false;
        }

        if (declaration is not ClassDeclarationSyntax and not RecordDeclarationSyntax)
        {
            return false;
        }

        if (declaration.Modifiers.Any(modifier =>
                modifier.IsKind(SyntaxKind.AbstractKeyword)
                || modifier.IsKind(SyntaxKind.StaticKeyword)))
        {
            return false;
        }

        return declaration.BaseList is { Types.Count: > 0 }
            || declaration.AttributeLists.Count > 0;
    }
}
