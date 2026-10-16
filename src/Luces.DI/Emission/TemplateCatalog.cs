using System;
using System.IO;
using System.Text;

namespace Luces.DI.Generators.Emission;

/// <summary>
/// Loading and placeholder substitution for the embedded source templates. The templates are embedded via the csproj <c>EmbeddedResource</c>, with logical names shaped like <c>Luces.DI.Templates.&lt;filename&gt;</c>.
/// </summary>
internal static class TemplateCatalog
{
    private const string ResourceRoot = "Luces.DI.Templates.";

    private static readonly Lazy<string> DefinitionsTemplate =
        new Lazy<string>(() => Load("DiRegistration.Definitions.cs"));

    private static readonly Lazy<string> RegistrationTemplate =
        new Lazy<string>(() => Load("GeneratedDiRegistrations.cs"));

    /// <summary>
    /// The definitions template: injects the marker interfaces and <c>[Register*]</c> attributes.
    /// </summary>
    public static string Definitions()
    {
        return DefinitionsTemplate.Value;
    }

    /// <summary>
    /// The registration template: the skeleton of the generated <c>GeneratedDiRegistrations</c> extension class.
    /// </summary>
    public static string RegistrationSource()
    {
        return RegistrationTemplate.Value;
    }

    /// <summary>
    /// Replaces a placeholder that must exist; a missing one indicates a packaging mismatch.
    /// </summary>
    public static string Substitute(string template, string token, string value)
    {
        if (template.IndexOf(token, StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException("Embedded template is missing placeholder: " + token);
        }

        return template.Replace(token, value);
    }

    /// <summary>
    /// Replaces a placeholder that must "occupy its own line": it takes that line's indentation and re-lays the multi-line value under the same indentation. If the placeholder's line has any other non-whitespace content, it is treated as a template error.
    /// </summary>
    public static string SubstituteLine(string template, string token, string value)
    {
        var tokenStart = template.IndexOf(token, StringComparison.Ordinal);
        if (tokenStart < 0)
        {
            throw new InvalidOperationException("Embedded template is missing placeholder: " + token);
        }

        var lineStart = template.LastIndexOf('\n', tokenStart) + 1;
        var lineFeed = template.IndexOf('\n', tokenStart);
        var lineEnd = lineFeed < 0 ? template.Length : lineFeed;

        var indentation = template.Substring(lineStart, tokenStart - lineStart);
        var trailing = template
            .Substring(tokenStart + token.Length, lineEnd - tokenStart - token.Length)
            .TrimEnd('\r');

        if (indentation.Trim().Length != 0 || trailing.Trim().Length != 0)
        {
            throw new InvalidOperationException("Placeholder must occupy its own line: " + token);
        }

        var indented = value.Replace("\n", "\n" + indentation);
        return string.Concat(
            template.Substring(0, lineStart),
            indentation,
            indented,
            template.Substring(lineEnd));
    }

    private static string Load(string fileName)
    {
        var resourceName = ResourceRoot + fileName;
        using var stream = typeof(TemplateCatalog).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded template not found: " + resourceName);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
