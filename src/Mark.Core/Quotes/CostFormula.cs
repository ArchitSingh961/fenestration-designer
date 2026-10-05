using System.Globalization;
using System.Text;

namespace Mark.Core.Quotes;

/// <summary>
/// The formulas of a cost sheet: numbers, + − × ÷ and brackets, a window's values (<c>#PROFILECOST</c>, <c>#AREASQFT</c> …)
/// and the cost lines above (<c>@Profile Wastage</c>, also written <c>@Profile Wastage.value</c>), e.g.
/// <c>@Total Raw Material Cost + @Fabrication Labour + @Installation Labour</c>. Names are not case-sensitive.
/// </summary>
public static class CostFormula
{
    /// <summary>The values of one window a formula can use, with what they are.</summary>
    public static IReadOnlyList<(string Name, string Meaning)> Variables { get; } = new[]
    {
        ("PROFILECOST", "profiles (frame, mullions, sashes, mesh bars) from the library prices"),
        ("RICOST", "reinforcement (steel) from the library or the reinforcement rate"),
        ("HWCOST", "hardware: the library's hardware sets and the hardware rates per sash"),
        ("GLASSCOST", "glass from the library prices"),
        ("ACCCOST", "accessories: gaskets, cleats, screws and other consumables"),
        ("MESHCOST", "insect mesh at the mesh rate"),
        ("MATERIALCOST", "all of the above: the raw material cost"),
        ("EXTRACOST", "the design's own extra cost (set on the design)"),
        ("AREASQFT", "window area in sq. ft."),
        ("AREAM2", "window area in m²"),
        ("GLASSSQFT", "glass area in sq. ft."),
        ("GLASSM2", "glass area in m²"),
        ("PROFILEM", "metres of profile"),
        ("PROFILEFT", "running feet of profile"),
        ("PERIMETERM", "the window's outline in metres"),
        ("SASHES", "openable sashes"),
        ("WIDTH", "window width in mm"),
        ("HEIGHT", "window height in mm"),
        ("QTY", "how many of the design")
    };

    /// <summary>
    /// The material lines every cost sheet starts with (before its own cost lines), and the value each one is. Formulas
    /// can use them as cost lines too: <c>@Profile Cost</c> is <c>#PROFILECOST</c>.
    /// </summary>
    public static IReadOnlyList<(string Name, string Variable)> MaterialLines { get; } = new[]
    {
        ("Profile Cost", "PROFILECOST"),
        ("RI Cost", "RICOST"),
        ("Hardware Cost", "HWCOST"),
        ("Glass Cost", "GLASSCOST"),
        ("Accessories Cost", "ACCCOST"),
        ("Mesh Cost", "MESHCOST")
    };

    /// <summary>Square feet in a square metre, and feet in a metre.</summary>
    public const decimal SquareFeetPerSquareMetre = 10.763910416709722m;
    public const decimal FeetPerMetre = 3.280839895013123m;

    private static readonly HashSet<string> Known = Variables.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Why <paramref name="formula"/> cannot be worked out, or null when it is fine.</summary>
    /// <param name="heads">The cost lines it may use: the ones above it.</param>
    public static string? Check(string? formula, IReadOnlyCollection<string> heads)
    {
        if (string.IsNullOrWhiteSpace(formula)) return "is empty";
        try
        {
            var parser = new Parser(formula, heads, _ => 1m, _ => 1m, checkOnly: true);
            parser.Run();
            return null;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Works out <paramref name="formula"/>; a formula that is not valid gives 0 (it was checked when it was saved).</summary>
    public static decimal Evaluate(string? formula, IReadOnlyCollection<string> heads, Func<string, decimal> variable, Func<string, decimal> head)
    {
        if (string.IsNullOrWhiteSpace(formula)) return 0;
        try
        {
            return new Parser(formula, heads, variable, head, checkOnly: false).Run();
        }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException or OverflowException)
        {
            return 0;
        }
    }

    /// <summary>The cost lines a formula uses (as named in <paramref name="heads"/>).</summary>
    public static IReadOnlyList<string> HeadsUsed(string? formula, IReadOnlyCollection<string> heads)
    {
        var used = new List<string>();
        if (string.IsNullOrWhiteSpace(formula)) return used;
        try
        {
            new Parser(formula, heads, _ => 1m, name => { used.Add(name); return 1m; }, checkOnly: false).Run();
        }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException or OverflowException)
        {
        }
        return used.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Recursive descent: sum := term (± term)*; term := factor (×÷ factor)*; factor := −factor | number | #VAR | @Head | (sum).</summary>
    private sealed class Parser
    {
        private readonly string _text;
        private readonly List<string> _heads;
        private readonly Func<string, decimal> _variable;
        private readonly Func<string, decimal> _head;
        private readonly bool _checkOnly;
        private int _at;

        public Parser(string text, IReadOnlyCollection<string> heads, Func<string, decimal> variable, Func<string, decimal> head, bool checkOnly)
        {
            _text = text;
            // Longest first, so "@Sub Total Including Labour" is not read as "@Sub Total" followed by text.
            _heads = heads.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).OrderByDescending(h => h.Length).ToList();
            _variable = variable;
            _head = head;
            _checkOnly = checkOnly;
        }

        public decimal Run()
        {
            decimal value = Sum();
            Skip();
            if (_at < _text.Length)
                throw new FormatException($"has \"{_text[_at..].Trim()}\" that is not understood");
            return value;
        }

        private decimal Sum()
        {
            decimal value = Term();
            while (true)
            {
                Skip();
                if (Take('+')) value += Term();
                else if (Take('-') || Take('−')) value -= Term();
                else return value;
            }
        }

        private decimal Term()
        {
            decimal value = Factor();
            while (true)
            {
                Skip();
                if (Take('*') || Take('×'))
                    value *= Factor();
                else if (Take('/') || Take('÷'))
                {
                    decimal by = Factor();
                    value = by == 0 ? (_checkOnly ? value : 0) : value / by;
                }
                else return value;
            }
        }

        private decimal Factor()
        {
            Skip();
            if (_at >= _text.Length) throw new FormatException("ends too early");
            char c = _text[_at];
            if (Take('-') || Take('−')) return -Factor();
            if (Take('+')) return Factor();
            if (Take('('))
            {
                decimal inner = Sum();
                Skip();
                if (!Take(')')) throw new FormatException("is missing a closing bracket");
                return inner;
            }
            if (char.IsDigit(c) || c == '.') return Number();
            if (c == '#') return Variable();
            if (c == '@') return Head();
            throw new FormatException($"has \"{c}\" where a number, #VALUE or @cost line was expected");
        }

        private decimal Number()
        {
            int start = _at;
            while (_at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] == '.')) _at++;
            string digits = _text[start.._at];
            if (!decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
                throw new FormatException($"has \"{digits}\" that is not a number");
            return value;
        }

        private decimal Variable()
        {
            _at++;
            int start = _at;
            while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] == '_')) _at++;
            string name = _text[start.._at];
            if (name.Length == 0) throw new FormatException("has a # without a name");
            if (!Known.Contains(name)) throw new FormatException($"uses #{name}, which is not a known value");
            return _variable(name.ToUpperInvariant());
        }

        private decimal Head()
        {
            _at++;
            string rest = _text[_at..];
            string? name = _heads.FirstOrDefault(h => rest.StartsWith(h, StringComparison.OrdinalIgnoreCase)
                                                      && (rest.Length == h.Length || !char.IsLetterOrDigit(rest[h.Length])));
            if (name is null)
            {
                var word = new StringBuilder();
                foreach (char ch in rest.TakeWhile(ch => ch is not ('+' or '-' or '*' or '/' or '(' or ')' or '.')))
                    word.Append(ch);
                throw new FormatException($"uses @{word.ToString().Trim()}, which is not a cost line above it");
            }
            _at += name.Length;
            if (_text.Length - _at >= 6 && string.Compare(_text, _at, ".value", 0, 6, StringComparison.OrdinalIgnoreCase) == 0)
                _at += 6;
            return _head(name);
        }

        private void Skip()
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at])) _at++;
        }

        private bool Take(char c)
        {
            if (_at < _text.Length && _text[_at] == c)
            {
                _at++;
                return true;
            }
            return false;
        }
    }
}
