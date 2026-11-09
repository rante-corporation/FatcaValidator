# FatcaValidator

`fatca-val` checks FATCA and CRS XML reports against the official IRS and OECD XML schemas before they
are submitted. It works offline: the schemas are compiled into the tool and nothing is fetched over the
network. Every error is reported with its line and position in the file.

| Reports | Bundled schema | Root element |
|---|---|---|
| FATCA (Form 8966, sent through IRS IDES) | IRS FATCA XML Schema v2.0.1 | `FATCA_OECD` in `urn:oecd:ties:fatca:v2` |
| CRS (the 2019 format) | OECD CRS XML Schema v2.0 | `CRS_OECD` in `urn:oecd:ties:crs:v2` |
| CRS (amended CRS, adopted by tax administrations from 1 January 2027) | OECD CRS XML Schema v3.0 | `CRS_OECD` in `urn:oecd:ties:crs:v3` |

The schema is chosen from the file's root element, so CRS v2.0 and v3.0 reports are each checked against
their own version. FATCA v2.0 reports are checked against v2.0.1, which accepts them: the IRS's own v2.0
sample reports pass. To use any other schema, such as a tax administration's own variant, pass its main
`.xsd` file with `--schema`.

## Usage

```
fatca-val --file <path> [--schema <path>] [--max-errors <count>]
```

| Option | Meaning |
|---|---|
| `-f`, `--file` | The FATCA or CRS XML file to validate. Required. |
| `-s`, `--schema` | A main `.xsd` file to use instead of the bundled schemas. The schemas it imports are read from its folder. |
| `-m`, `--max-errors` | How many errors, and separately how many warnings, to list. Default 100. The totals are always counted. |

For example, a FATCA report with an unknown country code and a missing `MessageRefId`:

```
> fatca-val --file report.xml
File:   C:\reports\report.xml
Schema: FATCA XML Schema v2.0.1 (IRS)
Result: INVALID - 2 errors, 0 warnings.
  error    Line 6, Position 32: The 'urn:oecd:ties:stffatcatypes:v2:TransmittingCountry' element is invalid - The value 'ZZ' is invalid according to its datatype 'urn:oecd:ties:isofatcatypes:v1:CountryCode_Type' - The Enumeration constraint failed.
  error    Line 10, Position 4: The element 'MessageSpec' in namespace 'urn:oecd:ties:fatca:v2' has invalid child element 'ReportingPeriod' in namespace 'urn:oecd:ties:stffatcatypes:v2'. List of possible elements expected: 'Warning, Contact, MessageRefId' in namespace 'urn:oecd:ties:stffatcatypes:v2'.
```

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Valid. |
| 1 | Invalid: the file is not well-formed XML, or it breaks its schema. |
| 2 | Not validated: bad arguments, an unreadable file, no bundled schema for the root element, or a schema that cannot be loaded. |

## What it checks, and what it does not

It checks that the file is well-formed and that it follows the schema completely: structure and order,
required elements, data types and lengths, and the code lists in the schemas (countries, currencies,
message and document type indicators, and so on).

It does not check the business rules that the IRS and tax administrations apply on top of the schema,
such as the format and uniqueness of `MessageRefId` and `DocRefId`, TIN formats, or correction and void
logic. A file that passes here can still be rejected after it is submitted.

## Security

- DTDs are refused, so there is no XML external entity (XXE) access and no entity expansion.
- `xsi:schemaLocation` hints in a file are ignored: a file cannot choose the schema it is checked against.
- The schema must declare the file's root element. (The .NET validator otherwise reports an undeclared root
  only as a warning, so a file of the wrong type would pass.)
- A schema given with `--schema` can only import files from its own folder; network locations are refused.
- Files are streamed, so a large report is never loaded into memory whole.

## Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet build
dotnet test
dotnet run --project src/FatcaValidator.Cli -- --file report.xml
```

### One native binary (Native AOT)

```
dotnet publish src/FatcaValidator.Cli -c Release -r win-x64
```

This produces a single self-contained executable, about 8 MB, that needs no .NET runtime on the machine:
`src/FatcaValidator.Cli/bin/Release/net10.0/win-x64/publish/fatca-val.exe`. For other platforms use
`-r linux-x64`, `-r osx-arm64` and so on, publishing on that operating system.

Native AOT needs the platform linker: on Windows, Visual Studio or the Build Tools with the "Desktop
development with C++" workload; on Linux, `clang` and `zlib1g-dev`. See the
[Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites).

If the Windows link step fails with `'vswhere.exe' is not recognized`, the Visual Studio Installer folder
is missing from `PATH`. Publish from a Developer PowerShell for Visual Studio, or add the folder for the
session first:

```powershell
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
```

## Use it from .NET code

```csharp
using FatcaValidator.Core;

var engine = new ValidatorEngine();
ValidationResult result = engine.Validate("report.xml");  // or engine.Validate("report.xml", "path/to/schema.xsd")

if (!result.IsValid)
{
    foreach (ValidationIssue issue in result.Errors)
    {
        Console.WriteLine(issue);  // Line 6, Position 32: ...
    }
}
```

`ValidationResult.Status` is `Valid`, `Invalid` or `NotValidated`. An engine compiles each schema once and
reuses it; it is not thread-safe, so use one engine per thread.

## Repository layout

```
FatcaValidator.sln
src/
  FatcaValidator.Core/      validation engine, no command-line dependencies
    ValidatorEngine.cs      streams a file through the schema validator
    BundledSchemas.cs       the bundled schema packages
    Schemas/                official IRS and OECD .xsd files, embedded in the assembly
  FatcaValidator.Cli/       the fatca-val command (System.CommandLine)
tests/
  FatcaValidator.Tests/     xUnit tests, with the IRS sample reports as test data
```

To bundle another schema package, such as a future FATCA or CRS version, follow
[Adding a package](src/FatcaValidator.Core/Schemas/README.md#adding-a-package).

## License

The code is licensed under the [MIT License](LICENSE). The bundled schema files are published by the IRS
and the OECD; [Schemas/README.md](src/FatcaValidator.Core/Schemas/README.md) lists their sources, hashes
and terms.
