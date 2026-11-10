namespace FatcaValidator.Tests;

/// <summary>Paths of the test documents and schema files copied next to the test assembly.</summary>
internal static class TestFiles
{
    /// <summary>A hand-written CRS v3.0 report (not an official OECD sample).</summary>
    public const string CrsSample = "crs-v3-individual-account.xml";

    /// <summary>A hand-written CRS v2.0 report (not an official OECD sample).</summary>
    public const string CrsV2Sample = "crs-v2-individual-account.xml";

    /// <summary>An IRS sample report from irs.gov/pub/fatca.</summary>
    public static string Irs(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Irs", name);

    /// <summary>A CRS test document.</summary>
    public static string Crs(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Crs", name);

    /// <summary>The main FATCA v2.0.1 schema file.</summary>
    public static string FatcaSchema => Path.Combine(AppContext.BaseDirectory, "Schemas", "Fatca", "v2.0.1", "FatcaXML_v2.0.1.xsd");

    /// <summary>An IRS sample notification (FATCA Notification XML v2.5).</summary>
    public static string IrsNotification(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Irs", "Notifications", name);

    /// <summary>A hand-written FATCA test file, such as the IDES metadata for newnilfireport.xml.</summary>
    public static string Fatca(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Fatca", name);

    /// <summary>The IRS's example FFI list file (FFIListExample.xml).</summary>
    public static string IrsFfiListExample => Path.Combine(AppContext.BaseDirectory, "TestData", "Irs", "FfiList", "FFIListExample.xml");

    /// <summary>A hand-written FFI list test file.</summary>
    public static string Ffi(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Ffi", name);

    /// <summary>A file from the repository's examples/rules folder.</summary>
    public static string ExampleRules(string name) => Path.Combine(AppContext.BaseDirectory, "Examples", name);

    /// <summary>Writes a copy of an IRS sample report with text replacements, failing the test if a text is missing.</summary>
    public static string WriteIrsVariant(TempFolder temp, string sample, params (string Find, string Replace)[] edits)
    {
        string text = File.ReadAllText(Irs(sample));
        foreach ((string find, string replace) in edits)
        {
            Assert.Contains(find, text, StringComparison.Ordinal);
            text = text.Replace(find, replace, StringComparison.Ordinal);
        }

        return temp.Write("variant-" + sample, text);
    }
}

/// <summary>A temporary folder, deleted when the test ends.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("fatcaval-").FullName;

    public string Write(string name, string content)
    {
        string path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the OS cleans the temp folder eventually.
        }
    }
}
