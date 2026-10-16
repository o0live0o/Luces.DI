using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;

namespace Luces.DI.Generators.Analysis;

/// <summary>
/// Type-shape checks for open generics together with the assignability check of "can an implementation be assigned to a service" (both share the same service-shape lookup, so they live here together).
/// </summary>
internal static class GenericShape
{
    /// <summary>
    /// The type itself or any containing type carries unbound/uninstantiated type parameters.
    /// </summary>
    public static bool HasOpenTypeParameters(INamedTypeSymbol type)
    {
        if (type.IsUnboundGenericType)
        {
            return true;
        }

        if (type.TypeArguments.Any(argument => argument.TypeKind == TypeKind.TypeParameter))
        {
            return true;
        }

        return type.ContainingType is not null
            && HasOpenTypeParameters(type.ContainingType);
    }

    /// <summary>
    /// Whether the implementation is assignable to the service: unbound generics use shape matching, everything else uses implicit conversion. User-defined conversions are excluded because <c>Add*&lt;TService, TImplementation&gt;</c> constrains <c>TImplementation</c> to be derived from <c>TService</c>, so such a pair would not compile.
    /// </summary>
    public static bool IsAssignableTo(
        Compilation compilation,
        INamedTypeSymbol implementation,
        INamedTypeSymbol service)
    {
        if (service.IsUnboundGenericType)
        {
            return FindServiceShape(implementation, service) is not null;
        }

        var conversion = ((CSharpCompilation)compilation)
            .ClassifyConversion(implementation, service);
        return conversion.Exists && conversion.IsImplicit && !conversion.IsUserDefined;
    }

    /// <summary>
    /// Preconditions for an open generic registration: neither side is nested inside an open generic containing type, the CLR generic shape matches on both sides (containing types included), and the implementation's type parameters correspond one-to-one with the service shape's type parameters (in the same order).
    /// </summary>
    public static bool CanRegisterAsOpenGeneric(
        INamedTypeSymbol implementation,
        INamedTypeSymbol service)
    {
        if (implementation.ContainingType is not null
            && HasOpenTypeParameters(implementation.ContainingType))
        {
            return false;
        }

        if (service.ContainingType is not null
            && HasOpenTypeParameters(service.ContainingType))
        {
            return false;
        }

        // The container maps one open generic type definition onto another, so the containing types' arities count as well: the unbound service form of "Worker<T> : Outer<int>.IService<T>" carries two type parameters where "Worker<>" carries one, and that mapping cannot be expressed.
        if (implementation.Arity == 0
            || service.Arity != implementation.Arity
            || GetTotalArity(service) != GetTotalArity(implementation))
        {
            return false;
        }

        var shape = FindServiceShape(implementation, service);
        if (shape is null
            || shape.TypeArguments.Length != implementation.TypeParameters.Length)
        {
            return false;
        }

        for (var index = 0; index < implementation.TypeParameters.Length; index++)
        {
            if (!SymbolEqualityComparer.Default.Equals(
                implementation.TypeParameters[index],
                shape.TypeArguments[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The number of type parameters the CLR sees for the type, counting the containing types as well.
    /// </summary>
    private static int GetTotalArity(INamedTypeSymbol type)
    {
        var total = 0;
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            total += current.Arity;
        }

        return total;
    }

    /// <summary>
    /// Finds the concrete shape corresponding to the service definition on the implementation itself, its base-type chain, and its interfaces.
    /// </summary>
    private static INamedTypeSymbol? FindServiceShape(
        INamedTypeSymbol implementation,
        INamedTypeSymbol service)
    {
        var definition = service.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(
            implementation.OriginalDefinition,
            definition))
        {
            return implementation;
        }

        for (var baseType = implementation.BaseType;
            baseType is not null;
            baseType = baseType.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(
                baseType.OriginalDefinition,
                definition))
            {
                return baseType;
            }
        }

        return implementation.AllInterfaces.FirstOrDefault(@interface =>
            SymbolEqualityComparer.Default.Equals(
                @interface.OriginalDefinition,
                definition));
    }
}
