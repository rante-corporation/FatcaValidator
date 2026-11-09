using System.CommandLine;
using System.CommandLine.Invocation;
using FatcaValidator.Core;

namespace FatcaValidator.Cli;

/// <summary>The exit codes of fatca-val.</summary>
internal static class ExitCodes
{
    /// <summary>The file is valid.</summary>
    public const int Valid = 0;

    /// <summary>The file is not well-formed XML, or it breaks its schema.</summary>
    public const int Invalid = 1;

    /// <summary>Nothing was validated: bad arguments, an unreadable file, an unknown document type or an unusable schema.</summary>
    public const int NotValidated = 2;
}

/// <summary>The fatca-val command line: parses the arguments, runs the validator and prints the result.</summary>
internal sealed class CliApp
{
    private readonly TextWriter _output;
    private readonly bool _useColor;

    private CliApp(TextWriter output, bool useColor)
    {
        _output = output;
        _useColor = useColor;
    }

    /// <summary>Runs the command line and returns the exit code (see <see cref="ExitCodes"/>).</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Where the result is written.</param>
    /// <param name="error">Where argument errors and unexpected failures are written.</param>
    /// <param name="useColor">Whether to color the result; only for a console that is not redirected.</param>
    public static int Run(string[] args, TextWriter output, TextWriter error, bool useColor)
    {
        Option<FileInfo> fileOption = new Option<FileInfo>("--file", "-f")
        {
            Description = "The FATCA or CRS XML file to validate.",
            HelpName = "path",
            Required = true,
        }.AcceptExistingOnly();

        Option<FileInfo> schemaOption = new Option<FileInfo>("--schema", "-s")
        {
            Description = "A main .xsd file to validate against instead of the bundled schemas; the schemas it imports "
                + "are read from its folder. Without it, the bundled IRS FATCA v2.0.1 or OECD CRS v3.0 schema is "
                + "chosen from the file's root element.",
            HelpName = "path",
        }.AcceptExistingOnly();

        var maxErrorsOption = new Option<int>("--max-errors", "-m")
        {
            Description = "How many errors, and separately how many warnings, to list. The totals are always counted.",
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

        var command = new RootCommand("Validates FATCA and CRS XML reports against the official IRS and OECD XML schemas.")
        {
            fileOption,
            schemaOption,
            maxErrorsOption,
        };

        var app = new CliApp(output, useColor);
        command.SetAction(parseResult => app.Validate(
            parseResult.GetRequiredValue(fileOption),
            parseResult.GetValue(schemaOption),
            parseResult.GetValue(maxErrorsOption)));

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

    private int Validate(FileInfo file, FileInfo? schema, int maxErrors)
    {
        var engine = new ValidatorEngine { MaxIssuesPerSeverity = maxErrors };
        ValidationResult result = engine.Validate(file.FullName, schema?.FullName);

        _output.WriteLine($"File:   {file.FullName}");
        _output.WriteLine($"Schema: {result.Schema ?? "(none)"}");
        _output.Write("Result: ");
        switch (result.Status)
        {
            case ValidationStatus.Valid:
                WriteColored("VALID", ConsoleColor.Green);
                _output.WriteLine(result.WarningCount == 0
                    ? " - no schema violations found."
                    : $" - no errors, {Count(result.WarningCount, "warning")}.");
                break;

            case ValidationStatus.Invalid:
                WriteColored("INVALID", ConsoleColor.Red);
                _output.WriteLine($" - {Count(result.ErrorCount, "error")}, {Count(result.WarningCount, "warning")}.");
                break;

            default:
                WriteColored("NOT VALIDATED", ConsoleColor.Yellow);
                _output.WriteLine();
                break;
        }

        foreach (ValidationIssue issue in result.Issues)
        {
            string label = result.Status == ValidationStatus.NotValidated ? ""
                : issue.Severity == ValidationSeverity.Error ? "error    "
                : "warning  ";
            _output.WriteLine($"  {label}{issue}");
        }

        WriteOmitted(result.ErrorCount - result.Errors.Count(), "error");
        WriteOmitted(result.WarningCount - result.Warnings.Count(), "warning");

        return result.Status switch
        {
            ValidationStatus.Valid => ExitCodes.Valid,
            ValidationStatus.Invalid => ExitCodes.Invalid,
            _ => ExitCodes.NotValidated,
        };
    }

    private void WriteOmitted(int omitted, string noun)
    {
        if (omitted > 0)
        {
            _output.WriteLine($"  ... and {Count(omitted, noun)} more (raise --max-errors to list them).");
        }
    }

    private void WriteColored(string text, ConsoleColor color)
    {
        if (!_useColor)
        {
            _output.Write(text);
            return;
        }

        Console.ForegroundColor = color;
        _output.Write(text);
        Console.ResetColor();
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
