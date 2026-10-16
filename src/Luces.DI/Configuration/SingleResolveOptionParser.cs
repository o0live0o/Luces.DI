using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Luces.DI.Generators.Configuration;

/// <summary>
/// Parses the value of <c>&lt;AutoDiSingleResolveServices&gt;</c>. Syntax: one or more contracts separated by <c>;</c> or <c>,</c>, each shaped like <c>Namespace.IType</c> or <c>Namespace.IType:2</c> (the suffix is the generic arity, defaulting to 0).
/// </summary>
internal static class SingleResolveOptionParser
{
    private static readonly char[] EntrySeparators = { ';', ',' };

    public static SingleResolveParseResult Parse(string? raw)
    {
        // netstandard2.0 reference assemblies carry no nullable annotations; an explicit null check avoids CS8602 here.
        var text = raw?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return SingleResolveParseResult.Empty;
        }

        var targets = ImmutableArray.CreateBuilder<SingleResolveTarget>();
        var invalid = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var segment in text.Split(EntrySeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = segment.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            if (!TryParseEntry(entry, out var target))
            {
                invalid.Add(entry);
                continue;
            }

            // Listing the same contract twice is not an error; only the first occurrence is kept.
            if (seen.Add(target.GetIdentity()))
            {
                targets.Add(target);
            }
        }

        return new SingleResolveParseResult(targets.ToImmutable(), invalid.ToImmutable());
    }

    /// <summary>
    /// Whether it is a plain identifier that can be safely placed into C# source (excluding keywords and contextual keywords).
    /// </summary>
    public static bool IsValidIdentifier(string value)
    {
        return SyntaxFacts.IsValidIdentifier(value)
            && SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None
            && SyntaxFacts.GetContextualKeywordKind(value) == SyntaxKind.None;
    }

    private static bool TryParseEntry(string entry, out SingleResolveTarget target)
    {
        target = default;

        var colonIndex = entry.LastIndexOf(':');
        string metadataName;
        int arity;
        if (colonIndex < 0)
        {
            metadataName = entry;
            arity = 0;
        }
        else
        {
            metadataName = entry.Substring(0, colonIndex).Trim();
            if (!int.TryParse(entry.Substring(colonIndex + 1).Trim(), out arity) || arity < 0)
            {
                return false;
            }
        }

        if (!IsValidMetadataName(metadataName))
        {
            return false;
        }

        target = new SingleResolveTarget(metadataName, arity);
        return true;
    }

    /// <summary>
    /// A contract name consists of plain identifiers separated by <c>.</c> or <c>+</c>, and is not written in arity backtick form.
    /// </summary>
    private static bool IsValidMetadataName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOf('`') >= 0)
        {
            return false;
        }

        foreach (var part in name.Split('.', '+'))
        {
            if (!IsValidIdentifier(part))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// A single single-resolve contract: metadata name plus generic arity.
/// </summary>
internal readonly record struct SingleResolveTarget(string MetadataName, int Arity)
{
    /// <summary>
    /// Deduplication identity.
    /// </summary>
    public string GetIdentity()
    {
        return MetadataName + ":" + Arity;
    }
}

internal readonly record struct SingleResolveParseResult(
    ImmutableArray<SingleResolveTarget> Targets,
    ImmutableArray<string> InvalidValues)
{
    public static readonly SingleResolveParseResult Empty = new(
        ImmutableArray<SingleResolveTarget>.Empty,
        ImmutableArray<string>.Empty);
}
