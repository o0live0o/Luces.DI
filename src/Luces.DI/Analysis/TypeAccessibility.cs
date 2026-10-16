using Microsoft.CodeAnalysis;

namespace Luces.DI.Generators.Analysis;

/// <summary>
/// Generated code lives at the top level of the consuming assembly, so referenced types must be visible to it; private / protected / private-protected (protected-and-internal) / file-local types, as well as generics with such type arguments, are all skipped.
/// </summary>
internal static class TypeAccessibility
{
    public static bool IsReferencableFromGeneratedCode(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (!IsAccessibleDeclaration(current))
            {
                return false;
            }

            // A nested type can be reachable itself while carrying an inaccessible type argument, e.g. Outer<T>.Inner instantiated with a private T.
            foreach (var argument in current.TypeArguments)
            {
                if (!IsReferencableTypeArgument(argument))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsAccessibleDeclaration(INamedTypeSymbol type)
    {
        if (type.IsFileLocal)
        {
            return false;
        }

        return type.DeclaredAccessibility is not (
            Accessibility.Private
            or Accessibility.Protected
            or Accessibility.ProtectedAndInternal);
    }

    private static bool IsReferencableTypeArgument(ITypeSymbol argument)
    {
        switch (argument)
        {
            case INamedTypeSymbol namedArgument:
                return IsReferencableFromGeneratedCode(namedArgument);
            case IArrayTypeSymbol arrayArgument:
                // An array of an inaccessible element type is itself unreachable (e.g. Hidden[]).
                return IsReferencableTypeArgument(arrayArgument.ElementType);
            default:
                // Type parameters and other shapes carry no accessibility of their own.
                return true;
        }
    }
}
