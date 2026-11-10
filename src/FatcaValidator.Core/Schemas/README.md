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

## FATCA Notification XML Schema v2.5 (IRS): `FatcaNotification/v2.5`

The notifications the IRS sends back after processing a FATCA file: valid-file notifications (with any record-level
errors) and file-level error notifications. Two bundled packages share this folder.

- **Source:** IRS, [FATCA XML schemas and business rules for Form 8966](https://www.irs.gov/businesses/corporations/fatca-xml-schemas-and-business-rules-for-form-8966),
  "FATCA Notification XML v2.5", effective 08-2020.
- **Package:** [`FATCA NOTIFICATION XML LIBRARY V2_5.zip`](https://www.irs.gov/pub/fatca/FATCA%20NOTIFICATION%20XML%20LIBRARY%20V2_5.zip),
  17,236 bytes, SHA-256 `6c018d31403fdcdc382505382d9e2a48c03acfa986b60e84925d4b643c41d186`, downloaded 2026-11-10. Its four
  sample notifications are the test data in `tests/FatcaValidator.Tests/TestData/Irs/Notifications`.
- **Terms:** a work of the U.S. federal government (17 U.S.C. § 105).

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `FATCA-VALID-FILE-NOTIFICATION-2.5.xsd` (main) | `urn:fatca:fatcavalidfilenotification` | 8,115 | `706bfb296b1943a7ca02e3e98f8e551845778682b17fb6448b83f67dbdf463dd` |
| `FATCA-FILE-ERROR-NOTIFICATION-2.5.xsd` (main) | `urn:fatca:fatcafileerrornotification` | 3,493 | `23d346d8d59948b07b1ee293fa5f9bcd28d0a8697848d470e2ef0d46fb611e4e` |
| `BASE-FATCA-NOTIFICATION-2.5.xsd` | `urn:fatca:fatcanotificationbase` | 102,044 | `abd48bfaa2b6ea60ead20f9526ffc89d66acceb896f758908812209e14a15533` |

## FATCA IDES Sender Metadata XML Schema v1.3 (IRS): `FatcaMetadata/v1.3`

The metadata file of an IDES data packet. The upload pre-flight (`--metadata`) validates it and compares it with the
report.

- **Source:** IRS, same page, "Sender Metadata XML v1.3", effective 07-2020: the schema the IRS names
  FATCA-IDES-SenderFileMetadata-1.3, published as [`fatca-ides-sender-file-metadata-1-3.xml`](https://www.irs.gov/pub/fatca/fatca-ides-sender-file-metadata-1-3.xml)
  and bundled under that name with an `.xsd` extension. The bytes are unchanged; downloaded 2026-11-10.
- **Terms:** a work of the U.S. federal government (17 U.S.C. § 105).

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `FATCA-IDES-SenderFileMetadata-1.3.xsd` | `urn:fatca:idessenderfilemetadata` | 18,729 | `bf7840fb1c40108acf555633418a9d31034920310194ffbbc13ef944565d5c4e` |

## OECD exchange formats

- **Source:** the OECD's XML schema packages for the automatic and spontaneous exchange of information
  ([OECD automatic exchange portal](https://www.oecd.org/tax/automatic-exchange/)), downloaded from oecd.org on 2026-11-10.
- **Terms:** © OECD. The OECD's [open-by-default policy](https://www.oecd.org/en/about/oecd-open-by-default-policy.html)
  allows reuse of its content with attribution. Attribution: OECD, the XML schema named in each row below.

| Folder | Schema | Package | Bytes | Package SHA-256 |
|---|---|---|---:|---|
| `Carf/v1.5` | Crypto-Asset Reporting Framework (CARF) XML Schema v1.5 | `xml-schema-carf-v1.5.zip` | 17,208 | `e55d533f88551d41ceec4f4b84258c6ff45116603c37cff3c11581097aa1898b` |
| `Cbc/v2.0` | Country-by-Country Reporting (CbC) XML Schema v2.0 | `cbc-schema-v2.0.zip` | 17,643 | `7e4a3ff1647b885e39d9dc93fd592d6370ba257c5d806b7d0380288408038911` |
| `CrsStatusMessage/v2.0` | CRS Status Message XML Schema v2.0 | `crs-status-message-v2.0.zip` | 8,617 | `20dbf04cfba775adb5e32b5e1d6373b5cf97dd6cf1a7b42f8b71a7e13d5826d9` |
| `CbcStatusMessage/v2.0` | CbC Status Message XML Schema v2.0 | `cbc-status-message-v2.0.zip` | 8,606 | `cf32edbb98b84aef014b28cb054d41a4201ca47c33a4a3fcaa381677f6e6be63` |
| `GenericStatusMessage/v2.0` | Generic Status Message XML Schema v2.0 | `generic-status-message-xml-schema-v2.0.zip` | 8,953 | `b9f81ad8fa6847b8039f472c53f55afff2d929a83e618fb3271571452f443b23` |
| `Etr/v2.0` | Exchange on Tax Rulings (ETR) XML Schema v2.0 | `etr-schema-v2.0.zip` | 17,664 | `57667b5ad3813a50e4c1180d4ae24147d4f1e69d923a20f5a03aedb5c584b874` |
| `Etr/v3.0` | Exchange on Tax Rulings (ETR) XML Schema v3.0 | `etr-schema-v3.0.zip` | 18,056 | `a5cfec832544e84828cffb5c63f161eb970e04d8fc50332f7be7dd01a519c3d4` |
| `EtrStatusMessage/v2.0` | ETR Status Message XML Schema v2.0 | `etr-status-message-v2.0.zip` | 8,609 | `be81ac0f0af8899cd64367cae7410a57c93b09ff03f4de3dee4a754e02931fca` |
| `Mdr/v1.0` | Mandatory Disclosure Rules (MDR) XML Schema v1.0 | `mdr-xml-schema-v1.0.zip` | 17,670 | `8e4a1fac1249d2631fcff8acebcc392d2eba9c8f280b3df785bf13060cffb897` |
| `Ntj/v1.0` | No or only Nominal Tax Jurisdictions (NTJ) XML Schema v1.0 | `ntj-schema-v1.0.zip` | 17,529 | `e3d422ca1a86203c0b2f38fe05b83d608f1f192aeb90ac8d27110ba23b10dcd7` |
| `Stf/v2.2` | Standard Transmission Format (STF) XML Schema v2.2 | `stf-v2.2.zip` | 20,156 | `a5974aa13df791f83219cb3485ef1ec462846459f27c6a374ce3713b8af9077f` |

### Crypto-Asset Reporting Framework (CARF) XML Schema v1.5: `Carf/v1.5`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `CARFXML_v1.5.xsd` | `urn:oecd:ties:carf:v1` | 33,066 | `26d78b9dab464a8ceb80cf08359cfa180456e36f53c2f2cd2e32a6c86a2cc775` |
| `isocarftypes_v1.1.xsd` | `urn:oecd:ties:isocarftypes:v1` | 106,396 | `1651976fcbb237085ed4a78305ae8609e708de4db5cc5c219e39f6b31ec511d7` |
| `oecdcarftypes_v5.0.xsd` | `urn:oecd:ties:carfstf:v5` | 9,554 | `f7ca31e2128c935e1658bb76ddb609734485f778cc9dd16901b713cbe718b8ed` |

### Country-by-Country Reporting (CbC) XML Schema v2.0: `Cbc/v2.0`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `CbcXML_v2.0.xsd` | `urn:oecd:ties:cbc:v2` | 25,920 | `305bfbaa2211f38cb192521e896be7c3fc9b20cf51f3c79cdc35d80d1f8897cd` |
| `isocbctypes_v1.1.xsd` | `urn:oecd:ties:isocbctypes:v1` | 106,102 | `4d363f553c9dc0aad250ef8a3997af7dcec00a74484792a04ddaa190ace6318b` |
| `oecdcbctypes_v5.0.xsd` | `urn:oecd:ties:cbcstf:v5` | 9,979 | `81315b00e934aee2442119e20acbc70f11001588ed9eb2005dc747abcbb2bd15` |

### CRS Status Message XML Schema v2.0: `CrsStatusMessage/v2.0`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `CrsStatusMessageXML_v2.0.xsd` | `urn:oecd:ties:csm:v2` | 12,728 | `2a35afa5f0c0100ab0d39bc6c9e310b4cdb0a9f3100588e8ec6a37e2f61dc3a5` |
| `isocsmtypes_v1.1.xsd` | `urn:oecd:ties:isocsmtypes:v1` | 70,743 | `46bd8a8d3622806fe6c84c8a3e1d494f0e0dbf82224822f64de0559858ffd1f9` |

### CbC Status Message XML Schema v2.0: `CbcStatusMessage/v2.0`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `CbcStatusMessageXML_v2.0.xsd` | `urn:oecd:ties:cbcstm:v2` | 12,718 | `1a6613c5dbd7fb311e67222424e7516b061239c64e0baf45767e5e48875afb77` |
| `isostmtypes_v1.1.xsd` | `urn:oecd:ties:cbcstm:isostmtypes:v1` | 70,749 | `e34628779c7db0dd3eca3a6b697407f2d9406454e7ba95022fa196f8e51f09cf` |

### Generic Status Message XML Schema v2.0: `GenericStatusMessage/v2.0`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `GenericStatusMessageXML_v2.0.xsd` | `urn:oecd:ties:gsm:v1` | 16,617 | `f86a248cf15712f09520ccffa09daeedadd7deb852f45994b106be8f106e7d75` |
| `isogsmtypes_v1.0.xsd` | `urn:oecd:ties:gsm:isogsmtypes:v1` | 70,744 | `7769d23227d6cbbd78a6f38cc1037c56b57a05ed6609f2f727dffd8e976c844e` |

### Exchange on Tax Rulings (ETR) XML Schema v2.0: `Etr/v2.0`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `EtrXML_v2.0.xsd` | `urn:oecd:ties:etr:v2` | 29,078 | `484cde01176c703c56d4b8cf5372594c45b46a917e03672cef8ebf47c7e364cf` |
| `isoetrtypes_v1.1.xsd` | `urn:oecd:ties:isoetrtypes:v1` | 105,877 | `4fce9091a99450864bfa8fa41d8e6732d7aa472c65b9cdc675a9b345cebf9fcc` |
| `oecdetrtypes_v5.0.xsd` | `urn:oecd:ties:etrstf:v5` | 7,991 | `b9aca8fc69f6e80aa1db826bdc8553a5a19a756fcaa4cd07ccafbe66109e78ed` |

### Exchange on Tax Rulings (ETR) XML Schema v3.0: `Etr/v3.0`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `EtrXML_v3.0.xsd` | `urn:oecd:ties:etr:v3` | 33,107 | `d5f4ee3f299f559c1f19ffb090f5a20857df637a0aa601f520b0cdc569ccc253` |
| `isoetrtypes_v1.1.xsd` | `urn:oecd:ties:isoetrtypes:v1` | 105,878 | `e5218143803bc360a75bf64376d87ecb4e878fb9c353a6584faff50def7fa9d4` |
| `oecdetrtypes_v5.0.xsd` | `urn:oecd:ties:etrstf:v5` | 8,276 | `817bcce4287ffe242c59de4de844ac587cae74466339f1c2f3a377fd9bb35065` |

### ETR Status Message XML Schema v2.0: `EtrStatusMessage/v2.0`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `EtrStatusMessageXML_v2.0.xsd` | `urn:oecd:ties:etrstm:v2` | 12,718 | `411f7eb38a01f1036390b9046f90eba3ff5f12eb2b638db10ccb833c839c9074` |
| `isostmtypes_v1.1.xsd` | `urn:oecd:ties:etrstm:isostmtypes:v1` | 70,749 | `f4d247898419fb3cd4db4217796a38f2017151a5777b45e6b26712a74ee7d639` |

### Mandatory Disclosure Rules (MDR) XML Schema v1.0: `Mdr/v1.0`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `IsoMdrTypes_v1.0.xsd` | `urn:oecd:ties:isomdrtypes:v1` | 104,466 | `9e36f7f3f8395ad6b1a5156057bc75b40869447fae92c6d1f97f3c97e5215141` |
| `MdrXML_v1.0.xsd` | `urn:oecd:ties:mdr:v1` | 35,949 | `30436dea212b5ea021ebe543faf90fb0288dbd8d9ff0b77201bc1f5e03f239f6` |
| `OecdMdrTypes_v1.0.xsd` | `urn:oecd:ties:mdrstf:v1` | 5,215 | `46c4c5943ab50741ae45d73f9924f68aaf85eb3326d18a884b276079083f525e` |

### No or only Nominal Tax Jurisdictions (NTJ) XML Schema v1.0: `Ntj/v1.0`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `IsoNtjTypes_v1.0.xsd` | `urn:oecd:ties:isontjtypes:v1` | 104,584 | `7bbaeba9688d688b59401b5cf9e025bfd02e76a1e3abf8d0b430bde438135c7d` |
| `NtjXML_v1.0.xsd` | `urn:oecd:ties:ntj:v1` | 30,800 | `4709fc578f661bc6488f341ffdc68f93d16e8fd03d160c84e9f160c233905eaf` |
| `OecdNtjTypes_v1.0.xsd` | `urn:oecd:ties:ntjstf:v1` | 7,707 | `7fcbaa5461df867e6eb987d12993d670edfe0ff670cd2918ace0f9151b724782` |

### Standard Transmission Format (STF) XML Schema v2.2: `Stf/v2.2`

| File | Target namespace | Bytes | SHA-256 |
|---|---|---:|---|
| `isotypes_v2.2.xsd` | `(none: included)` | 80,409 | `3f9752b4d23ea655d66a84f3007242fd7fd20c6808aee7231288b72f21aea57a` |
| `oecdtypes_v1.xsd` | `(none: included)` | 2,759 | `46d90e83b6c8d7883bd1382aaec9485bd3abb96d411d0601579f2765aa299f41` |
| `specifictypes_v2.xsd` | `(none: included)` | 766 | `788467258edd26e201296340ca206aa3e087d38cda7c453801a403f2eff6d7a8` |
| `stfdirect-2.2.xsd` | `urn:oecd:ties:stf:v2.2` | 4,180 | `9264c3774276a8bc171ba8e26acd84a825187fbbb138796d60cde3a85073874f` |
| `stftypes-2.2.xsd` | `urn:oecd:ties:stf:v2.2` | 37,967 | `9d1b6e7d6978bbb1aab9092b4981083bad0cbc9c9796c1da57be5cb5cdd69956` |

## Adding a package

1. Copy the official package's files, unchanged, into `Schemas/<Name>/<version>/`.
2. Add an `EmbeddedResource` line for that folder to `FatcaValidator.Core.csproj`.
3. Add a `SchemaPackage` with its main file and root element to `BundledSchemas`.
4. Record the source, terms and SHA-256 hashes here and in `SchemaIntegrityTests`.
