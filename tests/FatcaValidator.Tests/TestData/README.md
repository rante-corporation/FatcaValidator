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

## `Crs/`: hand-written CRS reports

`crs-v2-individual-account.xml` (CRS XML v2.0) and `crs-v3-individual-account.xml` (CRS XML v3.0) were
written for these tests from the schemas; they are not official OECD samples. The institution, person,
numbers and amounts in them are fictitious.
