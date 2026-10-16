using Luces.DI.Generators.Registration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Text;

namespace Luces.DI.Generators.Emission;

/// <summary>
/// Renders registrations into consumer-visible source: <see cref="BuildDefinitions"/> produces the definition-injection file, and <see cref="Emit"/> produces the extension-class file.
/// </summary>
internal static class RegistrationEmitter
{
    private const string DefinitionsNamespaceToken = "__DI_NAMESPACE__";
    private const string RootNamespaceToken = "__ROOT_NAMESPACE__";
    private const string RegistrationStatementsToken = "// __REGISTRATION_STATEMENTS__";

    /// <summary>
    /// Renders the source of the injected marker interfaces and <c>[Register*]</c> attributes.
    /// </summary>
    public static string BuildDefinitions(string definitionNamespace)
    {
        return TemplateCatalog.Substitute(
            TemplateCatalog.Definitions(),
            DefinitionsNamespaceToken,
            definitionNamespace);
    }

    /// <summary>
    /// Renders the source of the <c>GeneratedDiRegistrations</c> extension class.
    /// </summary>
    public static string Emit(
        Compilation compilation,
        ImmutableArray<RegisteredService> registrations)
    {
        var source = TemplateCatalog.Substitute(
            TemplateCatalog.RegistrationSource(),
            RootNamespaceToken,
            MakeNamespaceSafe(compilation.AssemblyName ?? "GeneratedAssembly"));

        return TemplateCatalog.SubstituteLine(
            source,
            RegistrationStatementsToken,
            BuildStatements(registrations));
    }

    private static string BuildStatements(ImmutableArray<RegisteredService> registrations)
    {
        var writer = new CodeWriter();
        if (registrations.IsDefaultOrEmpty)
        {
            writer.WriteLine(
                "// (no [Register*] / IAuto* targets discovered in this assembly)");
            return writer.ToTrimmedString();
        }

        foreach (var registration in registrations)
        {
            WriteStatement(writer, registration);
        }

        return writer.ToTrimmedString();
    }

    private static void WriteStatement(CodeWriter writer, RegisteredService registration)
    {
        // A registration is keyed whenever a key was supplied; the empty string is a valid key and must stay keyed.
        var isKeyed = registration.Key is not null;
        var keyLiteral = isKeyed
            ? SymbolDisplay.FormatLiteral(registration.Key!, quote: true)
            : string.Empty;

        var method = "Add"
            + (isKeyed ? "Keyed" : string.Empty)
            + registration.Lifetime.GetMethodSuffix();

        if (registration.IsOpenGeneric)
        {
            var arguments = isKeyed
                ? string.Concat(
                    "typeof(", registration.ServiceTypeName, "), ",
                    keyLiteral, ", typeof(", registration.ImplementationTypeName, ")")
                : string.Concat(
                    "typeof(", registration.ServiceTypeName, "), ",
                    "typeof(", registration.ImplementationTypeName, ")");
            writer.WriteLine(string.Concat("services.", method, "(", arguments, ");"));
            return;
        }

        var typeArguments = registration.ServiceTypeName == registration.ImplementationTypeName
            ? registration.ImplementationTypeName
            : string.Concat(
                registration.ServiceTypeName, ", ", registration.ImplementationTypeName);

        writer.WriteLine(
            string.Concat("services.", method, "<", typeArguments, ">(", keyLiteral, ");"));
    }

    /// <summary>
    /// Assembly name to namespace: each dot-separated segment must be a legal C# identifier, otherwise the emitted file would not compile (assembly names such as <c>9Acme.Tools</c> or <c>Acme.class</c>).
    /// </summary>
    private static string MakeNamespaceSafe(string assemblyName)
    {
        var segments = assemblyName.Split('.');
        var builder = new StringBuilder(assemblyName.Length);

        for (var index = 0; index < segments.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('.');
            }

            builder.Append(MakeIdentifierSafe(segments[index]));
        }

        return builder.ToString();
    }

    /// <summary>
    /// One namespace segment to a legal identifier: illegal characters become underscores, a leading digit gets an underscore prefix, and reserved keywords are escaped.
    /// </summary>
    private static string MakeIdentifierSafe(string segment)
    {
        var builder = new StringBuilder(segment.Length + 1);
        foreach (var character in segment)
        {
            builder.Append(
                char.IsLetterOrDigit(character) || character == '_'
                    ? character
                    : '_');
        }

        if (builder.Length == 0)
        {
            builder.Append('_');
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        var identifier = builder.ToString();
        return SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None
            ? identifier
            : "@" + identifier;
    }
}
