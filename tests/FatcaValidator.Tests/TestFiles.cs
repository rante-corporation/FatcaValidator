namespace FatcaValidator.Tests;

/// <summary>Paths of the test documents and schema files copied next to the test assembly.</summary>
internal static class TestFiles
{
    /// <summary>A hand-written CRS v3.0 report (not an official OECD sample).</summary>
    public const string CrsSample = "crs-v3-individual-account.xml";

    /// <summary>An IRS sample report from irs.gov/pub/fatca.</summary>
    public static string Irs(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Irs", name);

    /// <summary>A CRS test document.</summary>
    public static string Crs(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Crs", name);

    /// <summary>The main FATCA v2.0.1 schema file.</summary>
    public static string FatcaSchema => Path.Combine(AppContext.BaseDirectory, "Schemas", "Fatca", "v2.0.1", "FatcaXML_v2.0.1.xsd");
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
