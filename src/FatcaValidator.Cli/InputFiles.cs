namespace FatcaValidator.Cli;

/// <summary>Turns the --file values (files, folders, wildcards) into a list of files.</summary>
internal static class InputFiles
{
    private static readonly char[] Wildcards = ['*', '?'];

    /// <summary>
    /// Expands the inputs into full file paths, in order and without duplicates. A folder contributes its *.xml files
    /// (and its subfolders' with <paramref name="recursive"/>); a wildcard is allowed in the file name only.
    /// </summary>
    public static List<string> Expand(IEnumerable<string> inputs, bool recursive, List<string> errors)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        SearchOption depth = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (string input in inputs)
        {
            List<string> found;
            if (Directory.Exists(input))
            {
                found = Directory.EnumerateFiles(input, "*.xml", depth)
                    .Where(path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (found.Count == 0)
                {
                    errors.Add($"The folder '{input}' contains no .xml files{(recursive ? "" : " (add --recursive to include subfolders)")}.");
                    continue;
                }
            }
            else if (input.IndexOfAny(Wildcards) >= 0)
            {
                string folder = Path.GetDirectoryName(input) is { Length: > 0 } directory ? directory : ".";
                string pattern = Path.GetFileName(input);
                if (folder.IndexOfAny(Wildcards) >= 0 || !Directory.Exists(folder))
                {
                    errors.Add($"'{input}': wildcards work in the file name only, inside an existing folder.");
                    continue;
                }

                found = Directory.EnumerateFiles(folder, pattern, depth).Order(StringComparer.Ordinal).ToList();
                if (found.Count == 0)
                {
                    errors.Add($"'{input}' matches no files.");
                    continue;
                }
            }
            else if (File.Exists(input))
            {
                found = [input];
            }
            else
            {
                errors.Add($"'{input}' does not exist.");
                continue;
            }

            foreach (string path in found)
            {
                string full = Path.GetFullPath(path);
                if (seen.Add(full))
                {
                    files.Add(full);
                }
            }
        }

        return files;
    }
}
