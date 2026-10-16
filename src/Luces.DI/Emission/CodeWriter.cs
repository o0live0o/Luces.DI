using System;
using System.Text;

namespace Luces.DI.Generators.Emission;

/// <summary>
/// A minimal indentation writer for generated code: a fixed 4-space indent, using a <c>using</c> scope to manage braces and indentation levels so manual pairing cannot drift.
/// </summary>
internal sealed class CodeWriter
{
    private const int IndentWidth = 4;

    private readonly StringBuilder _buffer = new StringBuilder();
    private int _indent;

    /// <summary>
    /// Writes one line (an empty string is a blank line); blank lines produce no indentation whitespace.
    /// </summary>
    public CodeWriter WriteLine(string text)
    {
        if (text.Length > 0)
        {
            _buffer.Append(' ', _indent * IndentWidth).Append(text);
        }

        _buffer.Append('\n');
        return this;
    }

    /// <summary>
    /// Only increases the indentation, restored when the scope ends.
    /// </summary>
    public IndentScope Indent()
    {
        _indent++;
        return new IndentScope(this, closingBrace: false);
    }

    /// <summary>
    /// Writes the header and <c>{</c> and enters the indent; appends the <c>}</c> when the scope ends.
    /// </summary>
    public IndentScope Block(string header)
    {
        WriteLine(header);
        WriteLine("{");
        _indent++;
        return new IndentScope(this, closingBrace: true);
    }

    public override string ToString()
    {
        return _buffer.ToString();
    }

    /// <summary>
    /// Strips trailing newlines so the outer layer can do placeholder substitution directly.
    /// </summary>
    public string ToTrimmedString()
    {
        var end = _buffer.Length;
        while (end > 0 && (_buffer[end - 1] == '\n' || _buffer[end - 1] == '\r'))
        {
            end--;
        }

        return _buffer.ToString(0, end);
    }

    private void LeaveScope(bool closingBrace)
    {
        if (_indent == 0)
        {
            throw new InvalidOperationException("Indent scope is unbalanced; cannot exit.");
        }

        _indent--;
        if (closingBrace)
        {
            WriteLine("}");
        }
    }

    /// <summary>
    /// The scope handle returned by <see cref="Indent"/> and <see cref="Block"/>.
    /// </summary>
    internal readonly struct IndentScope : IDisposable
    {
        private readonly CodeWriter _owner;
        private readonly bool _closingBrace;

        internal IndentScope(CodeWriter owner, bool closingBrace)
        {
            _owner = owner;
            _closingBrace = closingBrace;
        }

        public void Dispose()
        {
            _owner.LeaveScope(_closingBrace);
        }
    }
}
