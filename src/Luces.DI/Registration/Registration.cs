using Microsoft.CodeAnalysis;

namespace Luces.DI.Generators.Registration;

/// <summary>
/// The service lifetimes supported by the generator.
/// </summary>
internal enum ServiceLifetime
{
    Scoped,
    Singleton,
    Transient,
}

internal static class ServiceLifetimeExtensions
{
    /// <summary>
    /// The lifetime suffix of the DI registration extension method name, e.g. Scoped → <c>AddScoped</c>.
    /// </summary>
    public static string GetMethodSuffix(this ServiceLifetime lifetime)
    {
        return lifetime switch
        {
            ServiceLifetime.Scoped => "Scoped",
            ServiceLifetime.Singleton => "Singleton",
            _ => "Transient",
        };
    }
}

/// <summary>
/// A single registration intent parsed from a <c>[Register*]</c> attribute.
/// </summary>
internal sealed record AttributeRegistration(
    ServiceLifetime Lifetime,
    INamedTypeSymbol? ExplicitService,
    string? Key,
    Location Location);

/// <summary>
/// One service registration to be generated. Type names are precomputed (including the open generic <c>&lt;&gt;</c> form) so the emission stage never has to touch symbols again.
/// </summary>
internal sealed record RegisteredService(
    INamedTypeSymbol ServiceSymbol,
    INamedTypeSymbol ImplementationSymbol,
    string ServiceTypeName,
    string ImplementationTypeName,
    ServiceLifetime Lifetime,
    string? Key,
    bool IsOpenGeneric)
{
    /// <summary>
    /// Deduplication key: service name + implementation name + lifetime + Key + open generic form. An unkeyed registration stays distinct from one keyed with the empty string, so both survive deduplication.
    /// </summary>
    public string GetIdentity()
    {
        return string.Concat(
            ServiceTypeName,
            "|",
            ImplementationTypeName,
            "|",
            Lifetime.GetMethodSuffix(),
            "|",
            Key is null ? "unkeyed" : string.Concat("keyed:", Key),
            "|",
            IsOpenGeneric ? "open" : "closed");
    }
}
