using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Threading;

namespace Luces.DI.Generators.Discovery;

/// <summary>
/// Syntax to candidate: converts a declaration that passed <see cref="CandidateSyntaxFilter"/> into the pair "symbol + location"; the symbol may be <c>null</c> because the semantic model is not ready yet.
/// </summary>
internal static class CandidateFactory
{
    public static Candidate Create(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(
            declaration,
            cancellationToken) as INamedTypeSymbol;

        return new Candidate(symbol, declaration.Identifier.GetLocation());
    }
}

/// <summary>
/// A type declaration awaiting further analysis.
/// </summary>
internal readonly record struct Candidate(INamedTypeSymbol? Symbol, Location Location);
