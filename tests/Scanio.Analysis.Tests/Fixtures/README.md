# Structured analysis fixture policy

Scanio's fixture suite uses compact, synthetic payloads that exercise published data-format structures without containing personal or production marking data.

Covered format families:

- GS1 element strings in parenthesized and raw group-separator forms;
- serialized GS1 DataMatrix marking payloads, including ambiguous local product-group classification;
- EAN-8, UPC-A, and EAN-13 values with valid and invalid check digits;
- IATA BCBP mandatory sections, multiple legs, truncation, and preserved conditional data;
- absolute HTTP/HTTPS URLs and rejected unsafe schemes;
- arbitrary decoded text as the final fallback.

Every structured family has a malformed sibling. Tests assert that malformed-but-recognizable data remains attached to its structured analyzer with validation messages. Tests also assert that payload-only evidence never populates physical barcode symbology.

Control separators are represented as `\u001D` in C# strings. Fixture decoding always starts from an explicit byte sequence so future file-based fixtures can preserve exact bytes without newline normalization.

Primary references used for structural boundaries:

- [GS1 Application Identifiers](https://www.gs1.org/gs1-application-identifiers) and the [GS1 DataMatrix Guideline](https://www.gs1.org/standards/gs1-datamatrix-guideline/25);
- [IATA BCBP Implementation Guide, version 7](https://www.iata.org/contentassets/1dccc9ed041b4f3bbdcf8ee8682e75c4/2021_03_02-bcbp-implementation-guide-version-7-.pdf).

The IATA fixture grammar limits Format M to four legs and places the two-character hexadecimal variable-field size immediately after each repeated 35-character mandatory leg section. Conditional payload bytes are retained and labelled unsupported when Scanio does not yet decode the corresponding Resolution 792 item set.
