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
          [--ffi-list <path>] [--history <path>] [--format text|json|sarif|junit] [--output <path>] [--max-errors <count>]
fatca-val history add --history <path> --file <path>... [--message <MessageRefId>]
fatca-val history list --history <path> [--format text|json] [--output <path>]
fatca-val explain --file <path>... [--report <path>]... [--history <path>] [--format text|json] [--output <path>]
```

The first form validates files; the [history](#submission-history) and [explain](#explain-notifications-and-status-messages)
commands are described below.

| Option | Meaning |
|---|---|
| `-f`, `--file` | The files to validate: file paths, folders (their `.xml` files) or wildcards in the file name, such as `reports/*.xml`. Repeat it, or list several. Required. |
| `--recursive` | Also take the `.xml` files in subfolders. |
| `-s`, `--schema` | A main `.xsd` file to use instead of the bundled schemas. The schemas it imports are read from its folder. |
| `-r`, `--rules` | A JSON file of your own business rules (see [Business rules](#business-rules)). Repeat it for several. |
| `--preflight` | The FATCA upload pre-flight (see [below](#fatca-upload-pre-flight)). |
| `--metadata` | The IDES sender metadata file that goes with the report. It implies `--preflight` and takes one `--file`. |
| `--ffi-list` | The IRS FFI list file, to check the report's GIINs against (see [below](#giin-check-against-the-irs-ffi-list)). |
| `--history` | Your submission history file, to check reports against the messages sent before (see [below](#submission-history)). |
| `--format` | `text` (the default), `json`, `sarif` or `junit`. |
| `-o`, `--output` | Write the report to a file instead of the console. |
| `-m`, `--max-errors` | How many errors, and separately how many warnings, to list per file. Default 100; the totals are always counted. |

```
fatca-val --file report.xml
fatca-val --file report.xml --preflight --metadata report-metadata.xml
fatca-val --file report.xml --rules acme.rules.json
fatca-val --file report.xml --ffi-list FFIListFullXml.zip
fatca-val --file report.xml --history fatca-history.json
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
| 1 | A file is not well-formed XML, breaks its schema, or fails a business rule, pre-flight or history check. |
| 2 | Something was not validated: bad arguments, an unreadable file, no bundled schema for a root element, or a schema, rule, FFI list or history file that cannot be loaded. |

`history add` exits with 1 when a report was not recorded because it fails a check, and with 2 when a file is not a
report, notification or status message, or a notification's message is not in the history. `explain` exits with 0 when
every file was accepted without errors, 1 when there is something to do, and 2 when a file is not a notification or
status message.

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

## GIIN check against the IRS FFI list

`--ffi-list` checks the GIINs in FATCA reports against the IRS's list of registered foreign financial institutions.
The IRS publishes the list on the 1st of each month on its
[FFI List Search and Download](https://apps.irs.gov/app/fatcaFfiList/flu.xhtml) page; pass its `FFIListFullXml.zip`, or the
`FFIListFull.xml` inside it, as it is.

| Check | Code |
|---|---|
| The GIIN is on the list: MessageSpec/SendingCompanyIN, ReportingFI/TIN, Sponsor/TIN, and Intermediary/TIN when it holds a GIIN | `FFI-GIIN-NOT-LISTED` |
| The sender, reporting FI and sponsor fields hold a well-formed GIIN (`XXXXXX.XXXXX.XX.XXX`, upper case) | `FFI-GIIN-FORMAT` |
| The reporting FI's, sponsor's and intermediary's names match the list (compared on letters and digits; one may contain the other) | `FFI-NAME-DIFFERS` |

All three are warnings. The list is a monthly snapshot: an institution approved after the list's cutoff only appears in
the next month's list. Tax authorities' GIINs (category `TA`, such as the IRS's `000000.00000.TA.840`) are not on the list
and are not looked up. The check runs offline and reads the list once per run, however many files you check: with the
November 2026 list (526,554 GIINs) a run took about 4 seconds and under 200 MB of memory in our tests. To keep every
month's list or compare months, see [rante-corporation/FatcaFfiList](https://github.com/rante-corporation/FatcaFfiList),
whose reader this check uses.

## Submission history

A `MessageRefId` and every `DocRefId` must be unique for all time, and a correction must name the latest version of a
record that was sent. One file cannot show that, so `fatca-val` can keep a local history of what you sent and what came
back, in one JSON file:

```
fatca-val history add --history fatca-history.json --file sent/report-2026-01.xml
fatca-val history add --history fatca-history.json --file inbox/notification.xml
fatca-val --file report-2026-02.xml --history fatca-history.json
fatca-val history list --history fatca-history.json
```

`history add` records a report only when it passes its schema and the history checks; recording the same file again
changes nothing. It records an IRS notification or an OECD status message with the message it names. IRS file error
notifications often cannot name one, so give it with `--message <MessageRefId>`. When reports and their notifications
are given together, the reports are recorded first.

With `--history`, each report that passes its schema is checked against the history:

| Check | Code | Severity |
|---|---|---|
| The MessageRefId was not used before. A file that was itself recorded is reported once. | `HIST-MESSAGEREFID-REUSED` | error |
| No DocRefId was used before. Reusing one from a message that was rejected as a whole is a warning: those records were never processed. | `HIST-DOCREFID-REUSED` | error or warning |
| A correction's CorrDocRefId is in the history. The record may predate the history, so this is a warning. | `HIST-CORR-UNKNOWN` | warning |
| CorrMessageRefId is the message that holds that record. | `HIST-CORR-MESSAGE-MISMATCH` | error |
| That record was not already corrected, amended or voided: a correction names the latest version. | `HIST-CORR-REPLACED` | error |
| That record's message was not rejected as a whole. | `HIST-CORR-REJECTED` | error |

Corrections are the records with `FATCA2`, `FATCA3` or `FATCA4`, or `OECD2` or `OECD3`, and their test codes. Resent
data (`OECD0`) is not checked for reuse. Test data (`FATCA11` to `FATCA14`, `OECD10` to `OECD13`) is checked against the
test messages in the history only, and production data against the production messages.

The history keeps each message's MessageSpec, each record's DocRefId, DocTypeIndic and correction references, the file
names and SHA-256 hashes, and the codes of the feedback received. It keeps no names, TINs, account numbers or amounts.
A message's status (sent, accepted or rejected) is that of the latest feedback recorded for it. The file is replaced in
one step when it is saved, so a failed write leaves the previous version.

## Explain notifications and status messages

`explain` turns IRS FATCA notifications (Notification XML v2.5) and OECD status messages (CRS, CbC, ETR and generic,
v2.0) into a to-do list: whether your file was accepted, and each file and record error with the authority's code, its
own text, and the action and due date the IRS asks for.

```
fatca-val explain --file notification.xml --report report.xml
```

For example, the IRS's sample record-level error notification (long texts shortened here):

```
> fatca-val explain --file RecordLevelErrorNotification_2.5.xml
File:    C:\inbox\RecordLevelErrorNotification_2.5.xml
Type:    FATCA Valid File Notification v2.5 (IRS)
Code:    NVF - Valid File Notification (N5)
Result:  ACCEPTED - the IRS processed the file; 1 record error to correct (the file had 1 record).
From:    000000.00000.TA.840 to 528L2G.00000.BR.152, reference 306, created 2017-01-23T20:12:17Z
About:   message EK9AMD70722-1, transmission a32be902284a41c593096a80633f0b94 sent 2017-01-22T20:08:18.18Z, file 20141210T004725848Z_528L2G.00000.BR.152.zip

The IRS says: The IRS has determined that the referenced file is in a valid format. ...

To do (1):
  1. Record error 8021
       Record:  ACCOUNT_REPORT 528L2G.00000.BR.152.DocRefIdEK9AMD70722-6 (FATCA1)
       Corrects: 528L2G.00000.BR.152.DocRefIdEK70722-6 in message EK70722-1
       Institution: 528L2G.00000.BR.152 (The BIG Chilialn BANK)
       Action:  Your file contains a report with an invalid DocRefID format. You should NOT attempt to amend, correct, or void the record as the record was not processed. ...
       Due:     Correct the errors within 120 days of the date of this notice.
```

With `--report`, each record error points at the record's line in the report you sent, matched by MessageRefId. With
`--history`, it says when that message was recorded as sent. The names of the IRS notification codes (`NVF`, `NSV`,
`NDM` and so on) come from the IRS notification schema. The IRS's record-level codes and the OECD's status message
codes are not in their schemas, so `explain` shows them with the texts the notification or status message gives.

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
indicators, and so on). With `--preflight` and `--rules`, it also checks the FATCA rules above and your own; with
`--ffi-list`, the GIINs against the IRS's list; with `--history`, the identifiers and corrections against what you sent
before.

It does not know every rule of every tax administration. It knows only the messages in your history: a correction of a
record sent before the history was started is a warning, not a finding. A file that passes here can still be rejected
after it is submitted.

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
    FfiList = FfiList.Load("FFIListFullXml.zip"),            // optional: GIINs against the IRS FFI list
    History = SubmissionHistory.Load("fatca-history.json"),  // optional: the messages sent before
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
rule, pre-flight, FFI list, history or validator), a severity, a line and a position. An engine compiles each schema
once and reuses it; it is not thread-safe, so use one engine (and its own rule sets) per thread.

`FeedbackDocument.Read("notification.xml")` reads an IRS notification or an OECD status message, and
`ReportSummary.Read("report.xml")` a report's MessageSpec and DocSpecs; `SubmissionHistory.AddMessage` and
`AddFeedback` record them.

## Repository layout

```
FatcaValidator.sln
src/
  FatcaValidator.Core/      validation engine, no command-line dependencies
    ValidatorEngine.cs      schema check, rules and pre-flight
    BundledSchemas.cs       the bundled schema packages
    Rules/                  the rule engine and the built-in IRS rules
    Preflight/              the FATCA upload pre-flight
    FfiList/                the GIIN check against the IRS FFI list (reader from FatcaFfiList)
    History/                the submission history, its checks and the report summary it keeps
    Feedback/               reading IRS notifications and OECD status messages
    Schemas/                official IRS and OECD .xsd files, embedded in the assembly
  FatcaValidator.Cli/       the fatca-val commands (System.CommandLine) and the report writers
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
