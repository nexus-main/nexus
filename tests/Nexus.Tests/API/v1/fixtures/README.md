# Source Configuration Schemas

`source-configuration-schemas.json` contains Draft 4 schemas returned by
`SourcesController.GetDescriptions()`. Angular tests can import this checked-in
JSON directly; no server or fixture generator is needed at frontend test time.
`SharedSchemaFixturesMatchServerOutput` checks the fixture against current server
output and prints the generated JSON on mismatch.

Root nullability comes from the source's base-type and interface declarations,
including generic substitutions, not from its configuration CLR `Type`.
Nullable roots use `anyOf` with a null branch and a strict non-null branch so that
recursive references do not become nullable accidentally.

Metadata availability still matters: `typeof(GenericSource<Config>)` cannot
describe whether a caller wrote a nullable reference type argument. Declare a
derived source (`class Source : GenericSource<Config?>`) to preserve that
annotation. Missing annotations (including nullable-disabled code) do not opt
typed roots into null. Dynamic assemblies or non-manifest modules without
accessible interface metadata fall back to the CLR contract when declaration
traversal cannot resolve it; nullable value types remain distinguishable.

## Flags And Formats

Numeric `[Flags]` enums use an open `integer` schema with inclusive `minimum` and
`maximum` matching the exact CLR underlying type. This is the runtime numeric
enum contract: named values, combinations (such as `1 | 2 = 3`), and unnamed bits
are accepted. Signed underlying types also accept negative values, including
`-1`; unsigned types reject negatives. Values outside the underlying range are
invalid. No finite list of bit combinations is generated.

Only numeric flags lose the validating `enum` constraint. `x-enumFlags: true`
and `x-enumNames` remain, and `x-enumValues` contains the corresponding named
numeric values for renderers. These annotations do not constrain validation.
Non-flags enums and string-converted enum schemas retain their `enum` lists.
Nullable flags retain their separate null alternatives.

Numeric `byte` uses `integer`, `minimum: 0`, `maximum: 255`, without `format`.
The `byte` format in OpenAPI/Ajv denotes a base64 string and cannot enforce a
numeric byte range. Binary `byte[]` schemas are not changed. Flags also rely on
explicit bounds rather than a numeric format. Other numeric mappings are not
changed by this correction.

The `formats` fixture captures the existing NJsonSchema mappings: `Guid` is a
string with `guid`; `ulong` an integer with `uint64`; `decimal` a number with
`decimal`; `TimeOnly` a string with `time`; `DateTime` a string with `date-time`.
Consumers need .NET-compatible format validation: `TimeOnly` has no offset and
`DateTime` permits local timestamps without a zone as well as `Z` and offsets.
The exact 64-bit bounds in JSON must not be confused with JavaScript's safe
integer range; rendering and round-tripping unsafe numbers is a separate
consumer concern, not a reason to narrow the server's CLR schema contract.
