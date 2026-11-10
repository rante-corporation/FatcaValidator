# Test data

## `Irs/`: IRS sample reports (official, unchanged)

FATCA XML v2.0 sample files published by the IRS on
[FATCA XML schemas and business rules for Form 8966](https://www.irs.gov/businesses/corporations/fatca-xml-schemas-and-business-rules-for-form-8966),
downloaded 2026-11-09. The tests check that they pass the bundled FATCA v2.0.1 schema, and edit copies
of them to produce invalid reports.

| File | Source | SHA-256 |
|---|---|---|
| `newnilfireport.xml` | https://www.irs.gov/pub/fatca/newnilfireport.xml | `70ff20e5a3e27cb9571e732a56028ce77f56445eac6e705d6837086abf1d66bb` |
| `amendednilfireport.xml` | https://www.irs.gov/pub/fatca/amendednilfireport.xml | `88206b7657c99ebff03100968541e59197fc9a2bf35d04ec9e33a46cd233d150` |
| `voidnilfireport.xml` | https://www.irs.gov/pub/fatca/voidnilfireport.xml | `a659213bfad7f04e364bad8c8cf26141caf21081686617bcac6fc96d14baf1a9` |
| `2014_2IW3WK.00000.LE.152.xml` | in https://www.irs.gov/pub/fatca/optional_elements_v2.0.zip | `4c6c5b5eb07ea9e006e762d17ac762e7c61919941aa97e89e70626a92fae0055` |
| `2016_2IW3WK.00000.LE.152.xml` | in https://www.irs.gov/pub/fatca/optional_elements_v2.0.zip | `7fab59587d890cb1edf411f670edc2a0ac994c40a51099918b0356930074947a` |

### `Irs/Notifications/`: IRS sample notifications (official, unchanged)

The four samples in the IRS's FATCA Notification XML v2.5 package
(https://www.irs.gov/pub/fatca/FATCA%20NOTIFICATION%20XML%20LIBRARY%20V2_5.zip, downloaded 2026-11-10).

| File | SHA-256 |
|---|---|
| `FieldLevelErrorNotification_2.5.xml` | `0226b4e6a8a98ff60397440e9389b8d24cd724a0321c7ec983d1449ae975e3a3` |
| `FileErrorNotificationSample_2.5.xml` | `9c1ee3984ca63f15508dd8615579d5308a7d51cf688ab4cd83db14d52dd5aeac` |
| `RecordLevelErrorNotification_2.5.xml` | `8814f11d31341f1d00e7611219bc8c60d8e9592f66e5e840a1f94d1660fd671c` |
| `ValidFileNotification_2.5.xml` | `2d617fdf6b17762e662e44dfcfee19d194685199f9656c2283fc10f4ec7929aa` |

### `Irs/FfiList/`: the IRS's example FFI list (official, unchanged)

`FFIListExample.xml` (3,687 bytes, SHA-256 `6ee471e3442772b300889abd519e428a51578ff387cf3de3372bc669cb47840d`) is the IRS's
sample FFI list file from its [FFI list schema and test files](https://www.irs.gov/businesses/international-businesses/irs-ffi-list-schema-and-test-files)
page, as kept in the tests of [rante-corporation/FatcaFfiList](https://github.com/rante-corporation/FatcaFfiList).

## `Ffi/`: hand-written FFI list

`ffi-list-irs-samples.xml` is a three-row list in the IRS's published format holding the GIINs of the IRS's sample
reports, written for the GIIN-check tests; the real list does not contain those sample GIINs.

## `Fatca/`: hand-written FATCA files

`newnilfireport-metadata.xml` is IDES sender metadata (v1.3) for the IRS sample `newnilfireport.xml`, written for the
pre-flight tests; it is not an IRS sample.

## `Crs/`: hand-written CRS reports

`crs-v2-individual-account.xml` (CRS XML v2.0) and `crs-v3-individual-account.xml` (CRS XML v3.0) were
written for these tests from the schemas; they are not official OECD samples. The institution, person,
numbers and amounts in them are fictitious.

`crs-status-message-record-error.xml` and `crs-status-message-rejected.xml` are CRS status messages (v2.0)
about `crs-v3-individual-account.xml`, written for the explain and history tests: one accepts it with a record
error, the other rejects it. Their codes (`EXAMPLE1`, `EXAMPLE2`) and texts are fictitious, not OECD codes.
