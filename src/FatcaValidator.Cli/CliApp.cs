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

/// <summary>
/// The fatca-val command line: parses the arguments, runs the validator and writes the report. The explain and
/// history commands are in CliApp.Explain.cs and CliApp.History.cs.
/// </summary>
internal sealed partial class CliApp
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
            // Required, but checked in Execute: a required option here would also be demanded by the explain and history commands.
            Description = "The XML files to validate: file paths, folders (their .xml files) or wildcards in the file name, such as reports/*.xml. Repeat it or list several.",
            HelpName = "path",
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

        Option<FileInfo> ffiListOption = new Option<FileInfo>("--ffi-list")
        {
            Description = "The IRS FATCA FFI list (FFIListFullXml.zip or FFIListFull.xml, published monthly by the IRS): checks the GIINs "
                + "of FATCA reports against it (sender, reporting FI, sponsors, intermediaries). Findings are warnings.",
            HelpName = "path",
        }.AcceptExistingOnly();

        Option<FileInfo> historyOption = new Option<FileInfo>("--history")
        {
            Description = "Your submission history file (see 'fatca-val history add'): reports are checked against the messages sent before. "
                + "A MessageRefId or DocRefId used before is an error, and corrections must name the latest version of a record that was sent.",
            HelpName = "path",
        }.AcceptExistingOnly();

        var formatOption = new Option<string>("--format")
        {
            Description = "The report format: text, json, sarif (for code-scanning tools) or junit (for CI test reports).",
            HelpName = "format",
            DefaultValueFactory = _ => "text",
        };
        formatOption.AcceptOnlyFromAmong("text", "json", "sarif", "junit");

        Option<FileInfo> outputOption = OutputOption();

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

        var app = new CliApp(output, error, useColor);
        var command = new RootCommand("Validates FATCA and CRS XML reports against the official IRS and OECD XML schemas, your own business rules and the FATCA upload pre-flight.")
        {
            fileOption,
            recursiveOption,
            schemaOption,
            rulesOption,
            preflightOption,
            metadataOption,
            ffiListOption,
            historyOption,
            formatOption,
            outputOption,
            maxErrorsOption,
            app.ExplainCommand(),
            app.HistoryCommand(),
        };

        command.SetAction(parseResult => app.Execute(new Settings(
            parseResult.GetValue(fileOption) ?? [],
            parseResult.GetValue(recursiveOption),
            parseResult.GetValue(schemaOption),
            parseResult.GetValue(rulesOption) ?? [],
            parseResult.GetValue(preflightOption),
            parseResult.GetValue(metadataOption),
            parseResult.GetValue(ffiListOption),
            parseResult.GetValue(historyOption),
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
        if (settings.Inputs.Length == 0)
        {
            _error.WriteLine("fatca-val: Option '--file' is required. Run 'fatca-val --help' for the options and commands.");
            return ExitCodes.NotValidated;
        }

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

        FfiList? ffiList = null;
        if (settings.FfiList is not null)
        {
            try
            {
                ffiList = FfiList.Load(settings.FfiList.FullName);
            }
            catch (FfiListException ex)
            {
                _error.WriteLine($"fatca-val: {ex.Message}");
                return ExitCodes.NotValidated;
            }
        }

        SubmissionHistory? history = null;
        if (settings.History is not null)
        {
            try
            {
                history = SubmissionHistory.Load(settings.History.FullName);
            }
            catch (HistoryException ex)
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
            FfiList = ffiList,
            History = history,
        };
        var options = new ValidationOptions { SchemaPath = settings.Schema?.FullName, MetadataPath = settings.Metadata?.FullName };
        var reports = files.Select(file => new FileReport(file, engine.Validate(file, options))).ToList();
        var run = new RunInfo(ffiList, history);

        IReportWriter writer = settings.Format switch
        {
            "json" => new JsonReportWriter(),
            "sarif" => new SarifReportWriter(),
            "junit" => new JUnitReportWriter(),
            _ => new TextReportWriter(_useColor && settings.Output is null),
        };

        if (!WriteReport(settings.Output, output => writer.Write(reports, run, output)))
        {
            return ExitCodes.NotValidated;
        }

        if (settings.Output is not null)
        {
            _output.WriteLine($"{Summary.Describe(reports)} Report: {settings.Output.FullName}");
        }

        return reports.Max(report => report.Result.Status switch
        {
            ValidationStatus.Valid => ExitCodes.Valid,
            ValidationStatus.Invalid => ExitCodes.Invalid,
            _ => ExitCodes.NotValidated,
        });
    }

    /// <summary>The --output option, shared by the commands that write a report.</summary>
    private static Option<FileInfo> OutputOption() => new("--output", "-o")
    {
        Description = "Write the report to this file instead of the console.",
        HelpName = "path",
    };

    /// <summary>Writes a report to the console, or to the --output file; false (after telling why) when the file cannot be written.</summary>
    private bool WriteReport(FileInfo? file, Action<TextWriter> write)
    {
        if (file is null)
        {
            write(_output);
            return true;
        }

        try
        {
            using var writer = new StreamWriter(file.FullName, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            write(writer);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _error.WriteLine($"fatca-val: the report could not be written to '{file.FullName}': {ex.Message}");
            return false;
        }
    }

    private sealed record Settings(
        string[] Inputs,
        bool Recursive,
        FileInfo? Schema,
        FileInfo[] RuleFiles,
        bool Preflight,
        FileInfo? Metadata,
        FileInfo? FfiList,
        FileInfo? History,
        string Format,
        FileInfo? Output,
        int MaxErrors);
}
