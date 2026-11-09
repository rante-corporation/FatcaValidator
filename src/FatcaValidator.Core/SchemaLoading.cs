using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace FatcaValidator.Core;

/// <summary>Compiles a main schema, and the schemas it imports, into an <see cref="XmlSchemaSet"/>.</summary>
internal static class SchemaSetLoader
{
    /// <exception cref="XmlSchemaException">A schema could not be read or does not compile. The message lists every problem.</exception>
    public static XmlSchemaSet Load(RestrictedSchemaResolver resolver, Uri mainSchema)
    {
        var problems = new List<string>();
        bool failed = false;
        var schemas = new XmlSchemaSet { XmlResolver = resolver };
        schemas.ValidationEventHandler += (_, e) =>
        {
            failed |= e.Severity == XmlSeverityType.Error;
            problems.Add(Describe(e));
        };

        var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using (Stream stream = Open(resolver, mainSchema))
        using (XmlReader reader = XmlReader.Create(stream, readerSettings, mainSchema.AbsoluteUri))
        {
            schemas.Add(null, reader);
        }

        schemas.Compile();
        if (failed)
        {
            // XmlSchemaSet reports a refused import only as "Cannot resolve the 'schemaLocation'
            // attribute", so the resolver's own reasons come first.
            throw new XmlSchemaException(string.Join(Environment.NewLine, resolver.Refusals.Concat(problems)));
        }

        return schemas;
    }

    private static Stream Open(XmlResolver resolver, Uri uri) =>
        resolver.GetEntity(uri, role: null, typeof(Stream)) as Stream
        ?? throw new XmlSchemaException($"'{uri}' could not be opened.");

    private static string Describe(ValidationEventArgs e)
    {
        XmlSchemaException ex = e.Exception;
        string location = string.IsNullOrEmpty(ex.SourceUri) ? "" : $"{ex.SourceUri} ({ex.LineNumber},{ex.LinePosition}): ";
        return location + e.Message;
    }
}

/// <summary>
/// A resolver that loads only what it allows and remembers why it refused anything else:
/// <see cref="XmlSchemaSet"/> drops the reason when a resolver throws.
/// </summary>
internal abstract class RestrictedSchemaResolver : XmlResolver
{
    private readonly List<string> _refusals = [];

    /// <summary>Why each refused location was not loaded, in order.</summary>
    public IReadOnlyList<string> Refusals => _refusals;

    /// <summary>Records the reason and returns the exception to throw.</summary>
    protected XmlSchemaException Refuse(string reason)
    {
        _refusals.Add(reason);
        return new XmlSchemaException(reason);
    }
}

/// <summary>
/// Reads bundled schemas from this assembly's embedded resources. Nothing else can be loaded,
/// so a bundled schema can only import other bundled schemas.
/// </summary>
internal sealed class EmbeddedSchemaResolver(Assembly assembly) : RestrictedSchemaResolver
{
    private const string Scheme = "embedded";

    /// <summary>The URI of an embedded resource, for example embedded:///Schemas/Fatca/v2.0.1/FatcaXML_v2.0.1.xsd.</summary>
    public static Uri UriFor(string resourceName) => new($"{Scheme}:///{resourceName}");

    public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativeUri);
        if (Uri.TryCreate(relativeUri, UriKind.Absolute, out Uri? absolute))
        {
            return absolute;
        }

        if (baseUri is not { IsAbsoluteUri: true })
        {
            throw new XmlSchemaException($"'{relativeUri}' cannot be resolved without a base location.");
        }

        return new Uri(baseUri, relativeUri);
    }

    public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        ArgumentNullException.ThrowIfNull(absoluteUri);
        if (absoluteUri.Scheme != Scheme)
        {
            throw Refuse($"'{absoluteUri}' was not loaded: bundled schemas can only import other bundled schemas.");
        }

        string name = Uri.UnescapeDataString(absoluteUri.AbsolutePath).TrimStart('/');
        return assembly.GetManifestResourceStream(name)
            ?? throw Refuse($"The bundled schema '{name}' is missing from {assembly.GetName().Name}.");
    }
}

/// <summary>
/// Reads a schema file given by the caller, and the schemas it imports, only from local files in
/// that schema's folder or below it. Nothing is fetched over the network and nothing else on disk is read.
/// </summary>
internal sealed class SchemaFolderResolver : RestrictedSchemaResolver
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _folder;

    public SchemaFolderResolver(string mainSchemaPath)
    {
        string fullPath = Path.GetFullPath(mainSchemaPath);
        string folder = Path.GetDirectoryName(fullPath) ?? fullPath;
        _folder = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
    }

    public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        ArgumentNullException.ThrowIfNull(absoluteUri);
        if (!absoluteUri.IsFile)
        {
            throw Refuse($"'{absoluteUri}' was not loaded: schemas are only read from local files, never over the network.");
        }

        string path = Path.GetFullPath(absoluteUri.LocalPath);
        if (!path.StartsWith(_folder, PathComparison))
        {
            throw Refuse($"'{path}' was not loaded: imported schemas must be in the main schema's folder ({_folder}) or below it.");
        }

        if (!File.Exists(path))
        {
            throw Refuse($"'{path}' was not found. Keep the schemas that a main schema imports next to it.");
        }

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
