# Business rules

`fatca-val` can run your own business rules after the schema check: the rules your institution, or the tax
administration you report to, applies on top of the XML schema. A rule file is JSON. Each rule is an XPath 1.0 check
with an id (reported as the error code), a severity and a message.

```json
{
  "name": "Acme Bank FATCA rules",
  "appliesTo": "urn:oecd:ties:fatca:v2",
  "namespaces": {
    "ftc": "urn:oecd:ties:fatca:v2",
    "sfa": "urn:oecd:ties:stffatcatypes:v2"
  },
  "rules": [
    {
      "id": "ACME-BALANCE",
      "severity": "error",
      "context": "//ftc:AccountReport/ftc:AccountBalance",
      "assert": "number(.) >= 0",
      "message": "Account {../ftc:AccountNumber} has a negative balance ({.} {@currCode})."
    },
    {
      "id": "ACME-ACCOUNT-ONCE",
      "severity": "warning",
      "unique": "//ftc:AccountReport/ftc:AccountNumber",
      "message": "Account {.} is reported more than once."
    }
  ]
}
```

```
fatca-val --file report.xml --rules acme.rules.json
```

`--rules` can be repeated. A failing rule is reported with its id, line and position:

```
  error    Line 48, Position 10: [ACME-BALANCE] Account NL00EXMP0123456789 has a negative balance (-12.50 USD).
```

[`examples/rules/fatca-example.rules.json`](../examples/rules/fatca-example.rules.json) is a starting point to copy.

## The file

| Property | Required | Meaning |
|---|---|---|
| `name` | no | Shown in reports. Defaults to the file name. |
| `description` | no | Documentation only. |
| `appliesTo` | no | A namespace URI, or an array of them. The rules run only on documents whose root element is in one of these namespaces; without it they run on every document. |
| `namespaces` | no | Prefix-to-URI pairs used in the XPath expressions. The prefix `fv` is reserved for the rule functions. |
| `rules` | yes | The rules, a non-empty array. |

Comments (`//` and `/* */`) and trailing commas are allowed.

## A rule

| Property | Required | Meaning |
|---|---|---|
| `id` | yes | Letters, digits, `.`, `_` and `-`. It is the error code in every report format, so keep it stable. |
| `severity` | no | `error` (the default) makes the file INVALID. `warning` is reported, and the file stays VALID. |
| `message` | yes | The message. `{xpath}` inserts a value, evaluated from the node that failed; write `{{` and `}}` for literal braces. Inserted values are cut at 200 characters. |
| `context` + `assert` | one of these two forms | `context` selects nodes; `assert` is evaluated for each of them and must be true. Each node where it is false is reported at its own line. |
| `unique` (+ `key`) | | `unique` selects nodes whose values must not repeat; `key` is an optional XPath, relative to each node, for the value to compare (by default the node's text). Every repeat is reported and points back to the line of the first one. Empty values are ignored. |
| `description`, `reference` | no | Documentation only, for example the guidance a rule comes from. |

## Functions

Everything in XPath 1.0 (`count`, `sum`, `starts-with`, `contains`, `normalize-space`, `string-length`, ...) plus:

| Function | Result |
|---|---|
| `fv:matches(text, pattern[, flags])` | True when the .NET regular expression matches anywhere in the text, so anchor it with `^` and `$` to match the whole value. Flags: `i` (ignore case), `m`, `s`, `x`. |
| `fv:ends-with(text, suffix)` | True when the text ends with the suffix. |
| `fv:lower-case(text)`, `fv:upper-case(text)` | The text in lower or upper case. |

Variables are not supported.

## When rules run

- Rule files are checked when they are loaded. Invalid JSON, an unknown property (a misspelled `asert`, say), invalid
  XPath, an undeclared prefix or an unknown function stops `fatca-val` with exit code 2 and a message naming the rule.
- Rules run after the schema check, and only on files that pass it. A file that breaks its schema gets a
  `RULES-SKIPPED` warning instead, because rules written for valid files would mostly report the same problem again.
- Rules need the whole document in memory, where the schema check streams it.

## Messages and personal data

Messages end up in console output, report files and CI logs. Quote reference numbers, such as account numbers or
DocRefIds, rather than names, TINs or dates of birth.

## The built-in IRS checks

`--preflight` runs a built-in rule set written in this same format,
[`src/FatcaValidator.Core/Rules/irs-fatca.rules.json`](../src/FatcaValidator.Core/Rules/irs-fatca.rules.json):

| Id | Severity | Check |
|---|---|---|
| `IRS-DOCREFID-LENGTH` | error | A DocRefId is 21 to 200 characters long. |
| `IRS-DOCREFID-CHARACTERS` | error | A DocRefId contains only letters, digits, periods and dashes. |
| `IRS-DOCREFID-GIIN` | warning | A DocRefId starts with the reporting FI's GIIN and a period (`<ReportingGIIN>.<UniqueValue>`). |
| `IRS-DOCREFID-UNIQUE` | error | No DocRefId repeats within the file. |
| `IRS-CORRDOCREFID-REQUIRED` | error | Corrected, void and amended records (FATCA2-4, FATCA12-14) carry the CorrDocRefId of the record they change. |
| `IRS-TEST-DATA` | warning | The file contains test data (FATCA11-14); reported once per file. |

The DocRefId rules come from the IRS page
[FATCA XML schemas best practices for Form 8966 DocRefId](https://www.irs.gov/businesses/corporations/fatca-xml-schemas-best-practices-for-form-8966-docrefid).
