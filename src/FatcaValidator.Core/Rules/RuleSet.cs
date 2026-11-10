using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace FatcaValidator.Core;

/// <summary>
/// Business rules loaded from a JSON rule file: XPath checks with their own ids (used as error codes), severities and
/// messages. They run on files that pass their schema. The format is described in docs/business-rules.md.
/// </summary>
/// <remarks>A rule set is not thread-safe: load one per thread.</remarks>
public sealed partial class RuleSet
{
    private static readonly HashSet<string> RuleSetProperties = ["name", "description", "appliesTo", "namespaces", "rules"];
    private static readonly HashSet<string> RuleProperties = ["id", "severity", "message", "context", "assert", "unique", "key", "description", "reference"];
    private static readonly XPathDocument Probe = new(new StringReader("<probe/>"));

    private readonly IReadOnlyList<Rule> _rules;

    private RuleSet(string name, string source, IReadOnlyList<string> appliesTo, IReadOnlyList<Rule> rules, IssueSource issueSource)
    {
        Name = name;
        Source = source;
        AppliesTo = appliesTo;
        _rules = rules;
        IssueSource = issueSource;
    }

    /// <summary>The rule set's name.</summary>
    public string Name { get; }

    /// <summary>Where the rules came from: the full path of the rule file, or "built-in:" and a name.</summary>
    public string Source { get; }

    /// <summary>The root-element namespaces the rules apply to. Empty means every document.</summary>
    public IReadOnlyList<string> AppliesTo { get; }

    /// <summary>The ids of the rules, in file order.</summary>
    public IReadOnlyList<string> RuleIds => _rules.Select(rule => rule.Id).ToArray();

    internal IssueSource IssueSource { get; }

    /// <summary>Loads and compiles a rule file.</summary>
    /// <exception cref="RuleSetException">The file cannot be read, is not valid JSON, or a rule is invalid.</exception>
    public static RuleSet Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string fullPath = Path.GetFullPath(path);
        string json;
        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RuleSetException($"The rule file '{fullPath}' could not be read: {ex.Message}", ex);
        }

        return Parse(json, fullPath);
    }

    /// <summary>Compiles rules from JSON text. <paramref name="source"/> names it in messages.</summary>
    /// <exception cref="RuleSetException">The JSON is invalid or a rule is invalid.</exception>
    public static RuleSet Parse(string json, string source) => Parse(json, source, IssueSource.Rule);

    internal static RuleSet LoadEmbedded(string resourceName, IssueSource issueSource)
    {
        using Stream stream = typeof(RuleSet).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The built-in rule set '{resourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd(), "built-in:" + resourceName, issueSource);
    }

    /// <summary>Whether the rules apply to a document whose root element is in <paramref name="rootNamespace"/>.</summary>
    public bool AppliesToNamespace(string rootNamespace) =>
        AppliesTo.Count == 0 || AppliesTo.Contains(rootNamespace, StringComparer.Ordinal);

    internal void Evaluate(XPathNavigator document, IssueCollector issues)
    {
        foreach (Rule rule in _rules)
        {
            try
            {
                rule.Evaluate(document, issues, IssueSource);
            }
            catch (Exception ex) when (ex is XPathException or XsltException or RegexMatchTimeoutException or ArgumentException or InvalidOperationException)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"The rule could not be evaluated: {ex.Message}", 0, 0, rule.Id, IssueSource));
            }
        }
    }

    private static RuleSet Parse(string json, string source, IssueSource issueSource)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new RuleSetException($"{source}: not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetException($"{source}: the file must contain a JSON object with a \"rules\" array.");
            }

            RejectUnknownProperties(root, RuleSetProperties, $"{source}: ");
            string name = OptionalString(root, "name", source) ?? Path.GetFileNameWithoutExtension(source);
            IReadOnlyList<string> appliesTo = ReadAppliesTo(root, source);

            var context = new RuleContext();
            if (root.TryGetProperty("namespaces", out JsonElement namespaces))
            {
                if (namespaces.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleSetException($"{source}: \"namespaces\" must be an object of prefix: namespace-URI pairs.");
                }

                foreach (JsonProperty ns in namespaces.EnumerateObject())
                {
                    if (ns.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(ns.Value.GetString()) || !IsNcName(ns.Name))
                    {
                        throw new RuleSetException($"{source}: namespace prefix \"{ns.Name}\" needs a non-empty namespace URI and must be a valid XML name.");
                    }

                    context.AddNamespace(ns.Name, ns.Value.GetString()!);
                }
            }

            if (!root.TryGetProperty("rules", out JsonElement rulesElement) || rulesElement.ValueKind != JsonValueKind.Array || rulesElement.GetArrayLength() == 0)
            {
                throw new RuleSetException($"{source}: \"rules\" must be a non-empty array.");
            }

            var rules = new List<Rule>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement ruleElement in rulesElement.EnumerateArray())
            {
                index++;
                Rule rule = ParseRule(ruleElement, index, source, context);
                if (!ids.Add(rule.Id))
                {
                    throw new RuleSetException($"{source}: rule {index}: the id \"{rule.Id}\" is used twice.");
                }

                rules.Add(rule);
            }

            return new RuleSet(name, source, appliesTo, rules, issueSource);
        }
    }

    private static Rule ParseRule(JsonElement element, int index, string source, RuleContext context)
    {
        string where = $"{source}: rule {index}";
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new RuleSetException($"{where}: each rule must be a JSON object.");
        }

        string id = OptionalString(element, "id", where) ?? throw new RuleSetException($"{where}: \"id\" is required.");
        if (!RuleIdPattern().IsMatch(id))
        {
            throw new RuleSetException($"{where}: the id \"{id}\" may only contain letters, digits, '.', '_' and '-' (it is used as the error code).");
        }

        where = $"{source}: rule {index} (\"{id}\")";
        RejectUnknownProperties(element, RuleProperties, $"{where}: ");

        string severityText = OptionalString(element, "severity", where) ?? "error";
        ValidationSeverity severity = severityText.ToLowerInvariant() switch
        {
            "error" => ValidationSeverity.Error,
            "warning" => ValidationSeverity.Warning,
            _ => throw new RuleSetException($"{where}: \"severity\" must be \"error\" or \"warning\", not \"{severityText}\"."),
        };

        string messageText = OptionalString(element, "message", where) ?? throw new RuleSetException($"{where}: \"message\" is required.");
        MessageTemplate message = MessageTemplate.Parse(messageText, context, where);

        string? contextText = OptionalString(element, "context", where);
        string? assertText = OptionalString(element, "assert", where);
        string? uniqueText = OptionalString(element, "unique", where);
        string? keyText = OptionalString(element, "key", where);

        if (uniqueText is not null)
        {
            if (assertText is not null || contextText is not null)
            {
                throw new RuleSetException($"{where}: use either \"context\" with \"assert\", or \"unique\" (with an optional \"key\"), not both.");
            }

            XPathExpression select = CompileNodeSet(uniqueText, context, $"{where}: \"unique\"");
            XPathExpression? key = keyText is null ? null : Compile($"string({keyText})", context, $"{where}: \"key\"", keyText);
            return new Rule(id, severity, message, select, Assert: null, Key: key);
        }

        if (keyText is not null)
        {
            throw new RuleSetException($"{where}: \"key\" only goes with \"unique\".");
        }

        if (contextText is null || assertText is null)
        {
            throw new RuleSetException($"{where}: give \"context\" (the nodes to check) and \"assert\" (what must be true for each), or \"unique\".");
        }

        XPathExpression contextExpression = CompileNodeSet(contextText, context, $"{where}: \"context\"");
        XPathExpression assertExpression = Compile(assertText, context, $"{where}: \"assert\"", assertText);
        return new Rule(id, severity, message, contextExpression, assertExpression, Key: null);
    }

    private static XPathExpression CompileNodeSet(string text, RuleContext context, string what)
    {
        XPathExpression expression = Compile(text, context, what, text);
        if (expression.ReturnType != XPathResultType.NodeSet)
        {
            throw new RuleSetException($"{what}: '{text}' must select nodes (it returns a {expression.ReturnType.ToString().ToLowerInvariant()}).");
        }

        return expression;
    }

    internal static XPathExpression Compile(string text, RuleContext context, string what, string shownText)
    {
        XPathExpression expression;
        try
        {
            expression = XPathExpression.Compile(text);
            expression.SetContext(context);
        }
        catch (XPathException ex)
        {
            throw new RuleSetException($"{what}: '{shownText}' is not a valid XPath 1.0 expression: {ex.Message}", ex);
        }

        // Evaluate once on an empty document, so unknown prefixes and functions fail now and not on the first file.
        try
        {
            Probe.CreateNavigator().Evaluate(expression);
        }
        catch (Exception ex) when (ex is XPathException or XsltException or ArgumentException)
        {
            throw new RuleSetException($"{what}: '{shownText}' cannot be evaluated: {ex.Message}", ex);
        }

        return expression;
    }

    private static IReadOnlyList<string> ReadAppliesTo(JsonElement root, string source)
    {
        if (!root.TryGetProperty("appliesTo", out JsonElement value))
        {
            return [];
        }

        IEnumerable<JsonElement> items = value.ValueKind switch
        {
            JsonValueKind.String => [value],
            JsonValueKind.Array => value.EnumerateArray(),
            _ => throw new RuleSetException($"{source}: \"appliesTo\" must be a namespace URI or an array of them."),
        };

        var namespaces = new List<string>();
        foreach (JsonElement item in items)
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new RuleSetException($"{source}: \"appliesTo\" entries must be non-empty namespace URIs.");
            }

            namespaces.Add(item.GetString()!);
        }

        return namespaces;
    }

    private static string? OptionalString(JsonElement element, string name, string where)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new RuleSetException($"{where}: \"{name}\" must be a non-empty string.");
        }

        return value.GetString();
    }

    private static void RejectUnknownProperties(JsonElement element, HashSet<string> known, string where)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name))
            {
                throw new RuleSetException($"{where}unknown property \"{property.Name}\" (allowed: {string.Join(", ", known)}).");
            }
        }
    }

    private static bool IsNcName(string name)
    {
        try
        {
            return XmlConvert.VerifyNCName(name) == name;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex RuleIdPattern();

    private sealed record Rule(string Id, ValidationSeverity Severity, MessageTemplate Message, XPathExpression Select, XPathExpression? Assert, XPathExpression? Key)
    {
        public void Evaluate(XPathNavigator document, IssueCollector issues, IssueSource source)
        {
            XPathNodeIterator nodes = document.Select(Select);
            if (Assert is not null)
            {
                while (nodes.MoveNext())
                {
                    XPathNavigator node = nodes.Current!.Clone();
                    if (!RuleContext.AsBoolean(node.Evaluate(Assert)))
                    {
                        Report(node, issues, source, suffix: null);
                    }
                }

                return;
            }

            // unique: report every node whose value already appeared, pointing back at the first one.
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            while (nodes.MoveNext())
            {
                XPathNavigator node = nodes.Current!.Clone();
                string key = Key is null ? node.Value : RuleContext.AsString(node.Evaluate(Key));
                if (key.Length == 0)
                {
                    continue;
                }

                if (seen.TryGetValue(key, out int firstLine))
                {
                    Report(node, issues, source, suffix: firstLine > 0 ? $" (first used on line {firstLine})" : null);
                }
                else
                {
                    seen[key] = (node as IXmlLineInfo)?.LineNumber ?? 0;
                }
            }
        }

        private void Report(XPathNavigator node, IssueCollector issues, IssueSource source, string? suffix)
        {
            var position = node as IXmlLineInfo;
            issues.Add(new ValidationIssue(Severity, Message.Render(node) + suffix, position?.LineNumber ?? 0, position?.LinePosition ?? 0, Id, source));
        }
    }

    /// <summary>A rule message with {xpath} placeholders, evaluated as strings against the node that failed.</summary>
    private sealed class MessageTemplate
    {
        private const int MaxValueLength = 200;
        private readonly IReadOnlyList<object> _parts;   // string or XPathExpression

        private MessageTemplate(IReadOnlyList<object> parts) => _parts = parts;

        public static MessageTemplate Parse(string text, RuleContext context, string where)
        {
            var parts = new List<object>();
            var literal = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
                {
                    literal.Append(c);
                    i++;
                    continue;
                }

                if (c == '}')
                {
                    throw new RuleSetException($"{where}: \"message\" has a '}}' without a matching '{{' (write '}}}}' for a literal brace).");
                }

                if (c != '{')
                {
                    literal.Append(c);
                    continue;
                }

                int end = text.IndexOf('}', i + 1);
                if (end < 0)
                {
                    throw new RuleSetException($"{where}: \"message\" has a '{{' without a closing '}}' (write '{{{{' for a literal brace).");
                }

                string expression = text[(i + 1)..end].Trim();
                if (expression.Length == 0)
                {
                    throw new RuleSetException($"{where}: \"message\" has an empty {{}} placeholder.");
                }

                if (literal.Length > 0)
                {
                    parts.Add(literal.ToString());
                    literal.Clear();
                }

                parts.Add(Compile($"string({expression})", context, $"{where}: \"message\" placeholder", "{" + expression + "}"));
                i = end;
            }

            if (literal.Length > 0)
            {
                parts.Add(literal.ToString());
            }

            return new MessageTemplate(parts);
        }

        public string Render(XPathNavigator node)
        {
            var text = new StringBuilder();
            foreach (object part in _parts)
            {
                if (part is string literal)
                {
                    text.Append(literal);
                    continue;
                }

                string value = RuleContext.AsString(node.Evaluate((XPathExpression)part));
                text.Append(value.Length > MaxValueLength ? value[..MaxValueLength] + "..." : value);
            }

            return text.ToString();
        }
    }
}

/// <summary>A rule file could not be read or contains an invalid rule.</summary>
public sealed class RuleSetException : Exception
{
    /// <summary>Creates the exception.</summary>
    public RuleSetException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    public RuleSetException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
