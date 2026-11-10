using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace FatcaValidator.Core;

/// <summary>
/// The XPath context of a rule set: its namespace prefixes plus the rule functions in the <c>fv</c> namespace
/// (<c>fv:matches</c>, <c>fv:ends-with</c>, <c>fv:lower-case</c>, <c>fv:upper-case</c>). Variables are not supported.
/// </summary>
internal sealed class RuleContext : XsltContext
{
    public const string FunctionsNamespace = "urn:fatca-validator:functions";
    public const string FunctionsPrefix = "fv";

    public RuleContext()
        : base(new NameTable())
    {
        AddNamespace(FunctionsPrefix, FunctionsNamespace);
    }

    /// <summary>
    /// Refuses undeclared prefixes. .NET's default XPath context does the same; a custom context that returned null
    /// instead would make a name test such as //x:Y silently match nothing, and the rule would never fire.
    /// </summary>
    public override string? LookupNamespace(string prefix)
    {
        string? ns = base.LookupNamespace(prefix);
        if (ns is null && prefix.Length > 0)
        {
            throw new XPathException($"The namespace prefix '{prefix}' is not declared in \"namespaces\".");
        }

        return ns;
    }

    public override bool Whitespace => true;

    public override bool PreserveWhitespace(XPathNavigator node) => true;

    public override int CompareDocument(string baseUri, string nextbaseUri) => string.CompareOrdinal(baseUri, nextbaseUri);

    public override IXsltContextVariable ResolveVariable(string prefix, string name) =>
        throw new XPathException($"Variables are not supported in rules (${(prefix.Length > 0 ? prefix + ":" : "")}{name}).");

    public override IXsltContextFunction ResolveFunction(string prefix, string name, XPathResultType[] argTypes)
    {
        if (LookupNamespace(prefix) != FunctionsNamespace)
        {
            string written = prefix.Length > 0 ? $"{prefix}:{name}" : name;
            throw new XPathException($"Unknown function '{written}()'. Rule functions are fv:matches, fv:ends-with, fv:lower-case and fv:upper-case.");
        }

        return name switch
        {
            "matches" => MatchesFunction.Instance,
            "ends-with" => EndsWithFunction.Instance,
            "lower-case" => CaseFunction.Lower,
            "upper-case" => CaseFunction.Upper,
            _ => throw new XPathException($"Unknown function 'fv:{name}()'. Rule functions are fv:matches, fv:ends-with, fv:lower-case and fv:upper-case."),
        };
    }

    /// <summary>The XPath string value of a function argument or an expression result.</summary>
    internal static string AsString(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        double d => double.IsNaN(d) ? "NaN" : d == Math.Floor(d) && Math.Abs(d) < 1e15 ? d.ToString("0", CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture),
        XPathNodeIterator it => it.MoveNext() && it.Current is not null ? it.Current.Value : "",
        XPathNavigator nav => nav.Value,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>The XPath boolean value of an expression result.</summary>
    internal static bool AsBoolean(object? value) => value switch
    {
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        XPathNodeIterator it => it.MoveNext(),
        _ => false,
    };

    private abstract class RuleFunction : IXsltContextFunction
    {
        public abstract int Minargs { get; }

        public abstract int Maxargs { get; }

        public abstract XPathResultType ReturnType { get; }

        public XPathResultType[] ArgTypes => [XPathResultType.String, XPathResultType.String, XPathResultType.String];

        public abstract object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext);
    }

    /// <summary>fv:matches(input, pattern[, flags]): true when the .NET regular expression matches anywhere in input.</summary>
    private sealed class MatchesFunction : RuleFunction
    {
        public static readonly MatchesFunction Instance = new();
        private static readonly ConcurrentDictionary<(string Pattern, string Flags), Regex> Cache = new();

        public override int Minargs => 2;

        public override int Maxargs => 3;

        public override XPathResultType ReturnType => XPathResultType.Boolean;

        public override object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
        {
            string input = AsString(args[0]);
            string pattern = AsString(args[1]);
            string flags = args.Length > 2 ? AsString(args[2]) : "";
            Regex regex = Cache.GetOrAdd((pattern, flags), static key => Create(key.Pattern, key.Flags));
            return regex.IsMatch(input);
        }

        private static Regex Create(string pattern, string flags)
        {
            RegexOptions options = RegexOptions.CultureInvariant;
            foreach (char flag in flags)
            {
                options |= flag switch
                {
                    'i' => RegexOptions.IgnoreCase,
                    'm' => RegexOptions.Multiline,
                    's' => RegexOptions.Singleline,
                    'x' => RegexOptions.IgnorePatternWhitespace,
                    _ => throw new XPathException($"fv:matches: unknown flag '{flag}' (use i, m, s or x)."),
                };
            }

            try
            {
                return new Regex(pattern, options, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException ex)
            {
                throw new XPathException($"fv:matches: '{pattern}' is not a valid regular expression: {ex.Message}");
            }
        }
    }

    /// <summary>fv:ends-with(string, suffix).</summary>
    private sealed class EndsWithFunction : RuleFunction
    {
        public static readonly EndsWithFunction Instance = new();

        public override int Minargs => 2;

        public override int Maxargs => 2;

        public override XPathResultType ReturnType => XPathResultType.Boolean;

        public override object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext) =>
            AsString(args[0]).EndsWith(AsString(args[1]), StringComparison.Ordinal);
    }

    /// <summary>fv:lower-case(string) and fv:upper-case(string), culture-invariant.</summary>
    private sealed class CaseFunction(bool upper) : RuleFunction
    {
        public static readonly CaseFunction Lower = new(false);
        public static readonly CaseFunction Upper = new(true);

        public override int Minargs => 1;

        public override int Maxargs => 1;

        public override XPathResultType ReturnType => XPathResultType.String;

        public override object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext) =>
            upper ? AsString(args[0]).ToUpperInvariant() : AsString(args[0]).ToLowerInvariant();
    }
}
