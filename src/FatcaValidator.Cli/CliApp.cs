using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using FatcaValidator.Cli.Reports;
using FatcaValidator.Core;

namespace FatcaValidator.Cli;

/// <summary>The exit codes of fatca-val. With several files, the worst status wins.</summary>
internal static class ExitCodes
{
    /// <summary>Every file is valid.</summary>
    public const int Valid = 0;

    /// <summary>A file is not well-formed XML, breaks its schema, or fails a business rule or pre-flight check.</summary>
    public const int Invalid = 1;

    /// <summary>Something was not validated: bad arguments, an unreadable file, an unknown document type or an unusable schema or rule file.</summary>
    public const int NotValidated = 2;
}

/// <summary>One validated file.</summary>
internal sealed record FileReport(string Path, ValidationResult Result);

/// <summary>The fatca-val command line: parses the arguments, runs the validator and writes the report.</summary>
internal sealed class CliApp
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly bool _useColor;

    private CliApp(TextWriter output, TextWriter error, bool useColor)
    {
        _output = output;
        _error = error;
        _useColor = useColor;
    }

    /// <summary>Runs the command line and returns the exit code (see <see cref="ExitCodes"/>).</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Where the report is written, unless --output names a file.</param>
    /// <param name="error">Where argument errors and unexpected failures are written.</param>
    /// <param name="useColor">Whether to color the text report; only for a console that is not redirected.</param>
    public static int Run(string[] args, TextWriter output, TextWriter error, bool useColor)
    {
        var fileOption = new Option<string[]>("--file", "-f")
        {
            Description = "The XML files to validate: file paths, folders (their .xml files) or wildcards in the file name, such as reports/*.xml. Repeat it or list several.",
            HelpName = "path",
            Required = true,
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };

        var recursiveOption = new Option<bool>("--recursive")
        {
            Description = "Also take the .xml files in the subfolders of folders and wildcards given with --file.",
        };

        Option<FileInfo> schemaOption = new Option<FileInfo>("--schema", "-s")
        {
            Description = "A main .xsd file to validate against instead of the bundled schemas; the schemas it imports are read from its folder. "
                + "Without it, the bundled schema (IRS FATCA, OECD CRS and others; see the README) is chosen from each file's root element.",
            HelpName = "path",
        }.AcceptExistingOnly();

        Option<FileInfo[]> rulesOption = new Option<FileInfo[]>("--rules", "-r")
        {
            Description = "A JSON file of your own business rules, run on files that pass their schema (see docs/business-rules.md). Repeat it for several.",
            HelpName = "path",
            AllowMultipleArgumentsPerToken = true,
        }.AcceptExistingOnly();

        var preflightOption = new Option<bool>("--preflight")
        {
            Description = "FATCA upload pre-flight: the checks IDES applies (UTF-8, the prohibited --, /* and &# patterns, the recommended maximum size) "
                + "and the IRS record checks (DocRefId format and uniqueness, correction references, test data).",
        };

        Option<FileInfo> metadataOption = new Option<FileInfo>("--metadata")
        {
            Description = "The IDES sender metadata file that goes with the report: it is validated and compared with the report. Implies --preflight; use one --file.",
            HelpName = "path",
        }.AcceptExistingOnly();

        var formatOption = new Option<string>("--format")
        {
            Description = "The report format: text, json, sarif (for code-scanning tools) or junit (for CI test reports).",
            HelpName = "format",
            DefaultValueFactory = _ => "text",
        };
        formatOption.AcceptOnlyFromAmong("text", "json", "sarif", "junit");

        var outputOption = new Option<FileInfo>("--output", "-o")
        {
            Description = "Write the report to this file instead of the console.",
            HelpName = "path",
        };

        var maxErrorsOption = new Option<int>("--max-errors", "-m")
        {
            Description = "How many errors, and separately how many warnings, to list per file. The totals are always counted.",
            HelpName = "count",
            DefaultValueFactory = _ => 100,
        };
        maxErrorsOption.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() < 1)
            {
                result.AddError("--max-errors must be at least 1.");
            }
        });

        var command = new RootCommand("Validates FATCA and CRS XML reports against the official IRS and OECD XML schemas, your own business rules and the FATCA upload pre-flight.")
        {
            fileOption,
            recursiveOption,
            schemaOption,
            rulesOption,
            preflightOption,
            metadataOption,
            formatOption,
            outputOption,
            maxErrorsOption,
        };

        var app = new CliApp(output, error, useColor);
        command.SetAction(parseResult => app.Execute(new Settings(
            parseResult.GetRequiredValue(fileOption),
            parseResult.GetValue(recursiveOption),
            parseResult.GetValue(schemaOption),
            parseResult.GetValue(rulesOption) ?? [],
            parseResult.GetValue(preflightOption),
            parseResult.GetValue(metadataOption),
            parseResult.GetValue(formatOption) ?? "text",
            parseResult.GetValue(outputOption),
            parseResult.GetValue(maxErrorsOption))));

        ParseResult parsed = command.Parse(args);
        int exitCode;
        try
        {
            exitCode = parsed.Invoke(new InvocationConfiguration
            {
                Output = output,
                Error = error,
                EnableDefaultExceptionHandler = false,
            });
        }
        catch (Exception ex)
        {
            error.WriteLine($"fatca-val: unexpected error: {ex}");
            return ExitCodes.NotValidated;
        }

        // System.CommandLine exits with 1 on argument errors, but 1 means "invalid file" here.
        return parsed.Action is ParseErrorAction ? ExitCodes.NotValidated : exitCode;
    }

    private int Execute(Settings settings)
    {
        var inputErrors = new List<string>();
        List<string> files = InputFiles.Expand(settings.Inputs, settings.Recursive, inputErrors);
        if (inputErrors.Count > 0)
        {
            foreach (string message in inputErrors)
            {
                _error.WriteLine($"fatca-val: {message}");
            }

            return ExitCodes.NotValidated;
        }

        if (settings.Metadata is not null && files.Count != 1)
        {
            _error.WriteLine($"fatca-val: --metadata describes one report, but --file gave {files.Count} files.");
            return ExitCodes.NotValidated;
        }

        var ruleSets = new List<RuleSet>();
        foreach (FileInfo ruleFile in settings.RuleFiles)
        {
            try
            {
                ruleSets.Add(RuleSet.Load(ruleFile.FullName));
            }
            catch (RuleSetException ex)
            {
                _error.WriteLine($"fatca-val: {ex.Message}");
                return ExitCodes.NotValidated;
            }
        }

        var engine = new ValidatorEngine
        {
            MaxIssuesPerSeverity = settings.MaxErrors,
            RuleSets = ruleSets,
            FatcaPreflight = settings.Preflight || settings.Metadata is not null,
        };
        var options = new ValidationOptions { SchemaPath = settings.Schema?.FullName, MetadataPath = settings.Metadata?.FullName };
        var reports = files.Select(file => new FileReport(file, engine.Validate(file, options))).ToList();

        IReportWriter writer = settings.Format switch
        {
            "json" => new JsonReportWriter(),
            "sarif" => new SarifReportWriter(),
            "junit" => new JUnitReportWriter(),
            _ => new TextReportWriter(_useColor && settings.Output is null),
        };

        if (settings.Output is null)
        {
            writer.Write(reports, _output);
        }
        else
        {
            try
            {
                using var file = new StreamWriter(settings.Output.FullName, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(reports, file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _error.WriteLine($"fatca-val: the report could not be written to '{settings.Output.FullName}': {ex.Message}");
                return ExitCodes.NotValidated;
            }

            _output.WriteLine($"{Summary.Describe(reports)} Report: {settings.Output.FullName}");
        }

        return reports.Max(report => report.Result.Status switch
        {
            ValidationStatus.Valid => ExitCodes.Valid,
            ValidationStatus.Invalid => ExitCodes.Invalid,
            _ => ExitCodes.NotValidated,
        });
    }

    private sealed record Settings(
        string[] Inputs,
        bool Recursive,
        FileInfo? Schema,
        FileInfo[] RuleFiles,
        bool Preflight,
        FileInfo? Metadata,
        string Format,
        FileInfo? Output,
        int MaxErrors);
}
