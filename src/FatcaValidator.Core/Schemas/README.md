# Bundled schemas

These are the official schema files, unchanged byte for byte. `FatcaValidator.Core` embeds them, so
the library and the `fatca-val` binary need no schema files next to them. `.gitattributes` stops git
from changing their line endings, and `SchemaIntegrityTests` checks each file's SHA-256 against the
tables below.

## FATCA XML Schema v2.0.1 (IRS): `Fatca/v2.0.1`

- **Source:** IRS, [FATCA XML schemas and business rules for Form 8966](https://www.irs.gov/businesses/corporations/fatca-xml-schemas-and-business-rules-for-form-8966),
  "FATCA XML v2.0.1", updated 2025-10-06.
- **Package:** [`fatcaxml-v2-0-1.zip`](https://www.irs.gov/pub/fatca/fatcaxml-v2-0-1.zip), 17,868 bytes,
  SHA-256 `f54dd75eea16831e8b85560147ec0fd9eb61f59bc88f767636fa13af6db440cd`, downloaded 2026-11-09.
- **Terms:** a work of the U.S. federal government, which is not subject to copyright in the United States
  (17 U.S.C. § 105).

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `FatcaXML_v2.0.1.xsd` (main) | `urn:oecd:ties:fatca:v2` | 26,342 | `bfab1daf0473aca250bf35831d58db3a16efb3804777cc8bb5313908ee5ba3d6` |
| `isofatcatypes_v1.2.xsd` | `urn:oecd:ties:isofatcatypes:v1` | 73,242 | `8d3fd18488acac640d9bc86f643c935bf6e2a7ef7aab19e4e4f7b9c26c58c86a` |
| `oecdtypes_v4.2.xsd` | `urn:oecd:ties:stf:v4` | 3,702 | `9a93ec6136c2904dc331003b91944d2d711440079b7867975c02aa45d4a5225d` |
| `stffatcatypes_v2.0.xsd` | `urn:oecd:ties:stffatcatypes:v2` | 24,675 | `3c2cebf3f19414d6e5998d4e09e59db4627f2d4ac074ec068557a76d5010521c` |

## CRS XML Schema v2.0 (OECD): `Crs/v2.0`

- **Source:** OECD, [Common Reporting Standard: schema and user guide](https://www.oecd.org/tax/automatic-exchange/common-reporting-standard/schema-and-user-guide/),
  "XML Schema - Version 2.0 (ZIP)", published with the CRS XML Schema User Guide for Tax Administrations (June 2019).
- **Package:** `crs-schema-v2.0.zip`, 17,549 bytes,
  SHA-256 `d770d7c426759691c57c89c41d808790f0f3b841e9cb61d514334c55ccb0a524`, downloaded from oecd.org on 2026-11-09.
- **Terms:** © OECD. The OECD's [open-by-default policy](https://www.oecd.org/en/about/oecd-open-by-default-policy.html)
  allows reuse of its content with attribution. Attribution: OECD, *Common Reporting Standard XML Schema*, version 2.0.

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `CrsXML_v2.0.xsd` (main) | `urn:oecd:ties:crs:v2` | 24,398 | `727a446aaad390de527b956ada05a1198943a5e2dc02c022c890aba3bd321bd1` |
| `CommonTypesFatcaCrs_v2.0.xsd` | `urn:oecd:ties:commontypesfatcacrs:v2` | 7,425 | `a56b1b123d1bf560e3715def76a73a6f5e93329b6094510487380be95b4b6eff` |
| `FatcaTypes_v1.2.xsd` | `urn:oecd:ties:fatca:v1` | 2,797 | `f842eca4f038cf3ee574f0820197c1448de69bc8200f926bb9d5c809417ebb97` |
| `isocrstypes_v1.1.xsd` | `urn:oecd:ties:isocrstypes:v1` | 76,045 | `aeed6deaf56cbf93415e8e17387f5b400add48ad573c462fb546cdd64f721281` |
| `oecdcrstypes_v5.0.xsd` | `urn:oecd:ties:crsstf:v5` | 9,108 | `80d2eb328f6a68b8da15a27f06366cea088b321115d57ceb56dcc2106096bf4f` |

`CommonTypesFatcaCrs_v2.0.xsd`, `FatcaTypes_v1.2.xsd` and `oecdcrstypes_v5.0.xsd` have the same names in the v2.0 and
v3.0 packages but different contents, so each package keeps its own folder and is compiled on its own.

## CRS XML Schema v3.0 (OECD): `Crs/v3.0`

- **Source:** OECD, [Amended Common Reporting Standard XML Schema](https://doi.org/10.1787/dd7ee57a-en)
  (CRS XML Schema v3.0, for the amended Common Reporting Standard).
- **Package:** `xml-schema-crs.zip`, 18,382 bytes,
  SHA-256 `71328994cf1e0924c119e78cf2e798bcb0bf6f2939c2f9ab7f3c5d2e0d7915e3`, obtained 2026-11-09. Its name
  and size match the "CRS XML Schema Version 3.0" download on the
  [Trinidad and Tobago Inland Revenue Division's CRS schema page](https://www.ird.gov.tt/CRS/schema).
- **Terms:** © OECD. The OECD's [open-by-default policy](https://www.oecd.org/en/about/oecd-open-by-default-policy.html)
  allows reuse of its content with attribution. Attribution: OECD, *Amended Common Reporting Standard XML Schema*,
  https://doi.org/10.1787/dd7ee57a-en.

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `CrsXML_v3.0.xsd` (main) | `urn:oecd:ties:crs:v3` | 32,549 | `aa25e255e85f15cf46b889fda3c2df5c290456c8686cac2896b917a9e3bcf925` |
| `CommonTypesFatcaCrs_v2.0.xsd` | `urn:oecd:ties:commontypesfatcacrs:v2` | 7,610 | `882d6cde408e915fc52b6ace3549a5cf4ddec161024f30f89b3ba400acbe6a80` |
| `FatcaTypes_v1.2.xsd` | `urn:oecd:ties:fatca:v1` | 2,796 | `b099b2cdeccc3d5cc0e22f13f312b313e077a59f0b6432d1ce9e7e189c411c72` |
| `isocrstypes_v1.1.xsd` | `urn:oecd:ties:isocrstypes:v1` | 76,045 | `aeed6deaf56cbf93415e8e17387f5b400add48ad573c462fb546cdd64f721281` |
| `oecdcrstypes_v5.0.xsd` | `urn:oecd:ties:crsstf:v5` | 9,108 | `11db640c16a7202ca6274d1c6bf6f9134e9f4863af280927c15e9ae2baf47dac` |

## Adding a package

1. Copy the official package's files, unchanged, into `Schemas/<Name>/<version>/`.
2. Add an `EmbeddedResource` line for that folder to `FatcaValidator.Core.csproj`.
3. Add a `SchemaPackage` with its main file and root element to `BundledSchemas`.
4. Record the source, terms and SHA-256 hashes here and in `SchemaIntegrityTests`.
