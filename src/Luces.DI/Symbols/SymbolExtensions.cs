using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Luces.DI.Generators.Symbols;

/// <summary>
/// Shared small utilities for symbol formatting and enumeration. Multiple places in the generator need a "fully qualified type name with namespace"; they are collected here to avoid scattered string concatenation.
/// </summary>
internal static class SymbolExtensions
{
    // FullyQualifiedFormat carries the global:: prefix, avoiding ambiguity introduced by the consumer's using directives.
    private static readonly SymbolDisplayFormat QualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    /// Produces the fully qualified type name in the form <c>global::Ns.Type</c>.
    /// </summary>
    public static string GetQualifiedName(this ITypeSymbol symbol)
    {
        return symbol.ToDisplayString(QualifiedFormat);
    }

    /// <summary>
    /// The fully qualified name in open generic form, e.g. <c>global::Ns.IRepo&lt;&gt;</c>.
    /// </summary>
    public static string GetUnboundQualifiedName(this INamedTypeSymbol symbol)
    {
        return symbol.OriginalDefinition.ConstructUnboundGenericType().GetQualifiedName();
    }

    /// <summary>
    /// The reflection-side metadata name (namespace plus containing types joined by <c>+</c>), used for comparison against strings hardcoded at compile time, e.g. <c>System.IEquatable`1</c>.
    /// </summary>
    public static string GetMetadataFullName(this INamedTypeSymbol symbol)
    {
        // Collect from inner to outer, then reverse, to get the "outer+inner" order.
        var names = new List<string> { symbol.MetadataName };
        for (var outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            names.Add(outer.MetadataName);
        }

        names.Reverse();
        var typePath = string.Join("+", names);
        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
        return string.IsNullOrEmpty(containingNamespace)
            ? typePath
            : containingNamespace + "." + typePath;
    }

    /// <summary>
    /// Depth-first enumeration of all types under a namespace (including nested types).
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> EnumerateDeclaredTypes(this INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            foreach (var entry in type.EnumerateSelfAndNestedTypes())
            {
                yield return entry;
            }
        }

        foreach (var child in root.GetNamespaceMembers())
        {
            foreach (var entry in child.EnumerateDeclaredTypes())
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSelfAndNestedTypes(this INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var entry in nested.EnumerateSelfAndNestedTypes())
            {
                yield return entry;
            }
        }
    }
}
