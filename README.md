# FatcaValidator

`fatca-val` checks FATCA, CRS and other OECD tax-exchange XML files against the official IRS and OECD XML schemas, your
own business rules and, for FATCA, the checks the IRS upload system (IDES) applies. It works offline: the schemas are
compiled into the tool and nothing is fetched over the network. Every finding has its line and position in the file,
and results come as text, JSON, SARIF or JUnit XML.

## Bundled schemas

| Files | Bundled schema | Root element (namespace) |
|---|---|---|
| FATCA reports (Form 8966, sent through IDES) | IRS FATCA XML v2.0.1 | `FATCA_OECD` (`urn:oecd:ties:fatca:v2`) |
| IRS notifications about a FATCA file | IRS FATCA Notification XML v2.5 | `FATCAValidFileNotification`, `FATCAFileErrorNotification` |
| IDES sender metadata | IRS IDES Sender Metadata v1.3 | `FATCAIDESSenderFileMetadata` |
| CRS reports, 2019 format | OECD CRS XML v2.0 | `CRS_OECD` (`urn:oecd:ties:crs:v2`) |
| CRS reports, amended CRS (from 1 January 2027) | OECD CRS XML v3.0 | `CRS_OECD` (`urn:oecd:ties:crs:v3`) |
| CRS status messages | OECD CRS Status Message v2.0 | `CRSStatusMessage_OECD` |
| Crypto-asset reports (CARF) | OECD CARF XML v1.5 | `CARF_OECD` |
| Country-by-country reports and their status messages | OECD CbC XML v2.0, CbC Status Message v2.0 | `CBC_OECD`, `CbCStatusMessage_OECD` |
| Tax rulings and their status messages | OECD ETR XML v2.0 and v3.0, ETR Status Message v2.0 | `ETR_OECD`, `ETRStatusMessage_OECD` |
| Mandatory disclosure (MDR) | OECD MDR XML v1.0 | `MDR_OECD` |
| No or only nominal tax jurisdictions (NTJ) | OECD NTJ XML v1.0 | `NTJ_OECD` |
| Generic status messages | OECD Generic Status Message v2.0 | `GenericStatusMessage_OECD` |
| Standard Transmission Format | OECD STF v2.2 | `STF_OECD` |

The schema is chosen from each file's root element and its namespace, so versions such as CRS v2.0 and v3.0 are each
checked against their own schema. FATCA v2.0 reports are checked against v2.0.1, which accepts them: the IRS's own v2.0
sample reports pass. For any other schema, pass its main `.xsd` file with `--schema`. Sources, terms and SHA-256 hashes
are in [Schemas/README.md](src/FatcaValidator.Core/Schemas/README.md).

## Usage

```
fatca-val --file <path>... [--recursive] [--schema <path>] [--rules <path>]... [--preflight] [--metadata <path>]
          [--format text|json|sarif|junit] [--output <path>] [--max-errors <count>]
```

| Option | Meaning |
|---|---|
| `-f`, `--file` | The files to validate: file paths, folders (their `.xml` files) or wildcards in the file name, such as `reports/*.xml`. Repeat it, or list several. Required. |
| `--recursive` | Also take the `.xml` files in subfolders. |
| `-s`, `--schema` | A main `.xsd` file to use instead of the bundled schemas. The schemas it imports are read from its folder. |
| `-r`, `--rules` | A JSON file of your own business rules (see [Business rules](#business-rules)). Repeat it for several. |
| `--preflight` | The FATCA upload pre-flight (see [below](#fatca-upload-pre-flight)). |
| `--metadata` | The IDES sender metadata file that goes with the report. It implies `--preflight` and takes one `--file`. |
| `--format` | `text` (the default), `json`, `sarif` or `junit`. |
| `-o`, `--output` | Write the report to a file instead of the console. |
| `-m`, `--max-errors` | How many errors, and separately how many warnings, to list per file. Default 100; the totals are always counted. |

```
fatca-val --file report.xml
fatca-val --file report.xml --preflight --metadata report-metadata.xml
fatca-val --file report.xml --rules acme.rules.json
fatca-val --file reports/ --recursive --format sarif --output results.sarif
```

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

With several files, the worst status wins.

| Code | Meaning |
|---|---|
| 0 | Every file is valid (warnings allowed). |
| 1 | A file is not well-formed XML, breaks its schema, or fails a business rule or pre-flight check. |
| 2 | Something was not validated: bad arguments, an unreadable file, no bundled schema for a root element, or a schema or rule file that cannot be loaded. |

## FATCA upload pre-flight

`--preflight` checks a FATCA report for what IDES and the IRS's processing reject, beyond the schema:

| Check | Code | Severity | Source |
|---|---|---|---|
| The file is UTF-8 | `IDES-ENCODING` | error | [IDES technical FAQs](https://www.irs.gov/businesses/corporations/fatca-ides-technical-faqs), C5 |
| No `--`, `/*` or `&#` in the text content (comments are skipped); IDES rejects these as possible SQL injection | `IDES-PROHIBITED-CHARACTERS` | error | IDES technical FAQs, C17; [FATCA XML schema best practices](https://www.irs.gov/businesses/corporations/fatca-xml-schema-best-practices-for-form-8966) |
| At most 200 MB compressed, the IRS's recommended maximum | `IDES-SIZE` | warning | IDES technical FAQs, C6 |
| DocRefId format and uniqueness, correction references, test data | `IRS-…` | error or warning | [the built-in IRS rules](docs/business-rules.md#the-built-in-irs-checks) |

With `--metadata`, the IDES sender metadata file is validated against its v1.3 schema and compared with the report:
the receiver is the IRS (`000000.00000.TA.840`), the communication type is `RPT`, the file format is `XML`, the binary
encoding is `NONE`, the tax year matches the reporting period, and the sender matches `SendingCompanyIN` (a warning).

The pre-flight does not check the signed, compressed and encrypted IDES data packet itself.

## Business rules

Your own checks go in a JSON rule file: each rule is an XPath check with an id (its error code), a severity and a
message that can quote values from the file. Rules run on files that pass their schema.

```json
{
  "appliesTo": "urn:oecd:ties:fatca:v2",
  "namespaces": { "ftc": "urn:oecd:ties:fatca:v2" },
  "rules": [
    {
      "id": "ACME-BALANCE",
      "context": "//ftc:AccountReport/ftc:AccountBalance",
      "assert": "number(.) >= 0",
      "message": "Account {../ftc:AccountNumber} has a negative balance ({.})."
    }
  ]
}
```

The format, the functions (`fv:matches` for regular expressions and others) and an example file are described in
[docs/business-rules.md](docs/business-rules.md).

## Reports

| Format | For |
|---|---|
| `text` | People: one block per file, then a summary line when there are several. |
| `json` | Scripts: a summary, then per file its status, schema, counts and issues (severity, code, source, line, position, message). |
| `sarif` | Code-scanning tools (SARIF 2.1.0): one result per issue, with the rule id and the file region. |
| `junit` | CI test reports: one test case per file; an invalid file fails, a file that could not be validated is an error. |

## What it checks, and what it does not

It checks that each file is well-formed and that it follows its schema completely: structure and order, required
elements, data types and lengths, and the code lists in the schemas (countries, currencies, message and document type
indicators, and so on). With `--preflight` and `--rules`, it also checks the FATCA rules above and your own.

It does not know every rule of every tax administration, and it cannot check facts outside the file, such as whether a
`CorrDocRefId` refers to a record that was sent before. A file that passes here can still be rejected after it is
submitted.

## Security

- DTDs are refused, so there is no XML external entity (XXE) access and no entity expansion.
- `xsi:schemaLocation` hints in a file are ignored: a file cannot choose the schema it is checked against.
- The schema must declare the file's root element. (The .NET validator otherwise reports an undeclared root only as a
  warning, so a file of the wrong type would pass.)
- A schema given with `--schema` can only import files from its own folder; network locations are refused.
- Files are streamed through the schema check, so a large report is not loaded into memory whole for it. Business rules
  and the pre-flight's record checks do load the document.

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

var engine = new ValidatorEngine
{
    FatcaPreflight = true,                                   // optional: the FATCA upload pre-flight
    RuleSets = [RuleSet.Load("acme.rules.json")],            // optional: your own rules
};

ValidationResult result = engine.Validate("report.xml", new ValidationOptions { MetadataPath = "report-metadata.xml" });
if (!result.IsValid)
{
    foreach (ValidationIssue issue in result.Errors)
    {
        Console.WriteLine(issue);  // Line 6, Position 32: ...
    }
}
```

`ValidationResult.Status` is `Valid`, `Invalid` or `NotValidated`; every issue has a `Code`, a `Source` (XML, schema,
rule, pre-flight or validator), a severity, a line and a position. An engine compiles each schema once and reuses it; it
is not thread-safe, so use one engine (and its own rule sets) per thread.

## Repository layout

```
FatcaValidator.sln
src/
  FatcaValidator.Core/      validation engine, no command-line dependencies
    ValidatorEngine.cs      schema check, rules and pre-flight
    BundledSchemas.cs       the bundled schema packages
    Rules/                  the rule engine and the built-in IRS rules
    Preflight/              the FATCA upload pre-flight
    Schemas/                official IRS and OECD .xsd files, embedded in the assembly
  FatcaValidator.Cli/       the fatca-val command (System.CommandLine) and the report writers
tests/
  FatcaValidator.Tests/     xUnit tests, with the IRS sample files as test data
docs/business-rules.md      the rule file format
examples/rules/             an example rule file
```

To bundle another schema package, follow
[Adding a package](src/FatcaValidator.Core/Schemas/README.md#adding-a-package).

## License

The code is licensed under the [MIT License](LICENSE). The bundled schema files are published by the IRS and the OECD;
[Schemas/README.md](src/FatcaValidator.Core/Schemas/README.md) lists their sources, hashes and terms.
