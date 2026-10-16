using Microsoft.CodeAnalysis;

namespace Luces.DI.Generators.Diagnostics;

/// <summary>
/// All diagnostics reported by the generator. Once published, IDs are an external contract and must not change arbitrarily; the severity is uniformly Error.
/// </summary>
internal static class GeneratorDiagnostics
{
    private const string Category = "Luces.SourceGen.DI";

    /// <summary>
    /// DIREG001: the same type declares multiple lifetimes.
    /// </summary>
    public static readonly DiagnosticDescriptor LifetimeConflict = new(
        id: "DIREG001",
        title: "Conflicting DI lifetimes on one type",
        messageFormat: "Type '{0}' declares conflicting DI lifetimes ({1}); exactly one lifetime may be applied per registration target",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DIREG002: a service configured as single-resolve has multiple implementations.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateSingleResolveImplementations = new(
        id: "DIREG002",
        title: "Multiple implementations for a single-resolve service",
        messageFormat: "Service '{0}' is declared single-resolve but has {1} implementations ({2}); a single-resolve service must map to exactly one implementation across the assembly graph",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DIREG003: the implementation or service type is unreachable from generated code.
    /// </summary>
    public static readonly DiagnosticDescriptor InaccessibleRegistrationType = new(
        id: "DIREG003",
        title: "DI registration type is inaccessible",
        messageFormat: "Type '{0}' cannot be referenced by the generated registration class and was skipped",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DIREG004: an open generic cannot be mapped safely.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedOpenGenericRegistration = new(
        id: "DIREG004",
        title: "Open generic DI registration is unsupported",
        messageFormat: "Type '{0}' cannot be registered as service '{1}': an open generic implementation and service must share arity and a directly matching type parameter order",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DIREG005: multiple service interfaces are implemented, so the service type cannot be inferred.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousServiceInference = new(
        id: "DIREG005",
        title: "DI service type cannot be inferred",
        messageFormat: "Type '{0}' implements multiple service interfaces ({1}); specify the intended service explicitly in [Register*]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DIREG006: a service configured as single-resolve has a source implementation missing a non-keyed registration.
    /// </summary>
    public static readonly DiagnosticDescriptor UnregisteredSingleResolveImplementation = new(
        id: "DIREG006",
        title: "Single-resolve implementation is not registered",
        messageFormat: "Type '{0}' implements the configured single-resolve service '{1}' but has no generated unkeyed registration. Add [Register*] or an IAuto* marker.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DIREG007: the MSBuild generator configuration is invalid.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidGeneratorConfiguration = new(
        id: "DIREG007",
        title: "Invalid Luces.DI generator configuration",
        messageFormat: "MSBuild property '{0}' has invalid value '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DIREG008: the explicitly configured service type is incompatible with the implementation.
    /// </summary>
    public static readonly DiagnosticDescriptor IncompatibleServiceType = new(
        id: "DIREG008",
        title: "DI service type is incompatible",
        messageFormat: "Type '{0}' is not assignable to the explicitly configured service '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DIREG009: the selected definition namespace already declares one of the injected type names, so the generated definitions would collide with the consumer's own declarations.
    /// </summary>
    public static readonly DiagnosticDescriptor DefinitionNamespaceConflict = new(
        id: "DIREG009",
        title: "Definition namespace already declares a generated type",
        messageFormat: "The definition namespace '{0}' already declares {1}, so the generated attributes, markers and registrations were skipped; remove the conflicting declaration or configure another namespace",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
