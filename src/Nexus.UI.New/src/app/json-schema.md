# JSON Schema Editor Contract

`JsonSchemaEditorComponent` (`app-json-schema-editor`) is a standalone controlled editor.

- Inputs: `schema: unknown`, `value: unknown`, `rawText: string | undefined`.
- Outputs: `valueChange: unknown`, `rawTextChange: string`, `validityChange: ConfigurationValidation`.
- A defined `rawText`, even an empty string, is authoritative. Store it per registration and bind it back. Set it to `undefined` only to load `value` instead.
- `valueChange` emits only safely parsed JSON, but that JSON may be schema-invalid. Invalid raw drafts emit only `rawTextChange` and invalid validity. A parent must validate all registrations, not just the open editor, before saving.
- Rendering, switching Form/JSON modes, and receiving inputs never create values, inject defaults, or emit value changes. Choosing Value from Null/Not set is an explicit creation action; clearing a string means `""`, not null or absence. Use the presence selector for Null/Not set. The root is always present.
- Root and field resets require inline confirmation before replacing existing data. Cancel emits no configuration changes. Input/draft changes cancel pending confirmation. The root field has no duplicate reset button.
- While a numeric input is focused, its local token (including `1.`, `-`, or `1e`) stays visible in the form. Every keystroke still emits a full-document raw draft and updates validity; only safe parsed values emit valueChange. The form uses a stable snapshot during typing so parent echoes cannot erase the local token. On blur, an invalid token switches to full-document JSON and stays there until Form is chosen, preventing other field edits from overwriting the invalid draft. Invalid non-numeric JSON field edits switch immediately. Buffers survive registration changes when bound by the parent; reopened invalid drafts appear in JSON mode.
- If embedding invalid field text could accidentally create valid different JSON (for example clearing the only array item, or entering `1,2` as one item), an invalid-JSON comment is appended. Repair the value and remove that comment explicitly. No rounded/reinterpreted value is emitted.

## Pipeline Transport Boundary

Use `parseJsonSafely` on the response text **before** any transport `JSON.parse` or `Response.json()` for **GET `/api/v1/sources/pipelines` only**. A lazy `import('./json-schema')` exposes the same pure helper used by raw editing. It returns `{ valid: true, value, errors: [] }` or `{ valid: false, errors: string[] }`. On failure, do not fall back to ordinary parsing.

Do not apply this parser to source descriptions/schema responses. NJsonSchema commonly includes Int64 minimum/maximum bounds outside JavaScript's safe-integer range. Schemas remain unmodified, and ordinary description transport parsing is retained. Ajv evaluates numeric bounds as JavaScript numbers; safe configuration integers cannot reach the usual Int64 endpoints, but arbitrary high-precision decimal schema constraints are not exact arithmetic.

The parser checks numeric tokens before JSON.parse without relying on the newer reviver context API. It rejects unsafe integers (even exactly representable integers outside the safe range), overflow, underflow, negative zero, and decimals whose normalized decimal value differs after a Number/string round trip. It accepts ordinary decimals such as `0.1`, not arbitrary-precision binary-exact arithmetic. Numeric values rounded by an earlier parser cannot be recovered. Duplicate JSON object names follow JSON.parse's last-name-wins behavior; this is not a general lossless JSON syntax tree.

`multipleOf` uses exact BigInt divisibility of normalized decimal Number round-trip tokens, including negative values and scientific notation. For example, `0.3` is a multiple of `0.1`, but `0.30000000000000004` is not. No epsilon tolerance is used. This does not recover precision already lost while parsing schema metadata; the divisor is interpreted from its received Number's decimal representation.

## Format Policy

Unknown formats fail closed with an explicit schema error, including formats in optional fields and definitions. `strict: false` is retained for NJsonSchema annotations/keywords, but does not permit silent unknown-format acceptance. `$ref` siblings are ignored under Draft 4 semantics, subject to the limitation below.

- Standard formats supplied by `ajv-formats` use its full validators unless overridden below: date, URI, email, hostname, IP, UUID, regex, JSON pointers, byte/base64, and its other registered standard formats. `guid` is an alias of the same UUID validator, including its accepted UUID/URN forms; it is not a general CLR Guid.Parse implementation.
- `time` accepts `hh:mm:ss[.fffffff]` (TimeOnly) with an optional `Z` or `+/-hh:mm` offset. `date-time` accepts `yyyy-MM-ddT` followed by that time, including timezone-free DateTime.Unspecified. Checks enforce the Gregorian calendar, years 0001-9999, hours 00-23, minutes/seconds 00-59, one to seven fractional digits, and offsets up to +/-14:00. Leap seconds, omitted seconds, lowercase separators, whitespace separators, compact offsets and fractional precision above seven digits are rejected. These are explicit .NET-aware lexical policies, not strict RFC3339 or full CLR parser emulation. Strings are never normalized or converted to Date objects. Offset-bearing time-only values may still require a backend type other than TimeOnly; date-time offset conversion at year boundaries is left to the backend.
- `time-span` validates the invariant .NET constant form `[-][d.]hh:mm:ss[.fffffff]`, including component ranges, up to seven fractional digits, and the signed Int64 tick limits using BigInt. Culture-specific forms and abbreviated hours/minutes are not supported.
- `duration` accepts either that .NET TimeSpan form (NJsonSchema/ASP.NET usage) or the ISO/RFC duration syntax supplied by `ajv-formats`. The latter accepts whole-component durations such as `P1D`, `PT1H`, and `P1W`; it does not accept fractional ISO seconds or negative ISO durations. No conversion or normalization is performed. This is an explicit union policy, not a guarantee of backend deserialization for every ISO duration.
- `int32` is range/integrality checked by `ajv-formats`. `int64` is integral and additionally subject to the configuration helper's safe-integer restriction. `uint64` validates nonnegative safe integers. The full .NET Int64/UInt64 ranges are deliberately unavailable as JSON numbers. Quoted numbers are permitted only when the original schema accepts strings.
- `decimal` is a known finite-number representation annotation subject to the same safe-integer and decimal round-trip policy, explicit schema bounds and exact decimal multipleOf checks. It does not promise CLR Decimal's 96-bit coefficient, scale 0-28 or full precision. For example `1e-29` is allowed unless constrained by the schema, but a high-precision raw decimal that would round is rejected. No implicit CLR Decimal range/scale constraints are injected.
- Numeric `byte` needs explicit `type: integer`, `minimum: 0`, `maximum: 255` in the backend-generated schema. Ajv's `byte` format validates base64 strings only; the UI does not rewrite schemas or claim numeric bounds from that format alone. Explicit bounds are tested, and string/base64 behavior is unchanged.
- `float` and `double` are known numeric representation annotations, not guarantees of CLR range or Float32 exactness. JSON number safety and explicit schema bounds still apply. `password` and `binary` are known string annotations, not content validators; the editor does not promise password masking or binary decoding. Schema `type`, `pattern`, and other constraints remain active.

## Other Limits

Validators are cached by root schema identity; supplied schemas must be treated as immutable. Missing schemas, unresolved refs, unsupported dialects and compilation failures fail closed. There is no remote ref fetching or save override.

OpenAPI `nullable` is not a Draft 4 keyword. Ajv applies it in its type compiler even when the keyword is removed, so schemas containing it fail closed (for both true and false) rather than silently gaining OpenAPI semantics. Use Draft 4 null type unions or anyOf/oneOf instead. Presence choices also fail closed for such schemas.

Ajv's legacy Draft 4 `$ref` sibling-ignore option still applies a sibling `type` incorrectly. Schemas containing that combination fail closed with an explicit message rather than applying incorrect semantics or rewriting the schema. Single-branch `oneOf`/`anyOf` ref wrappers generated by NJsonSchema are supported.

Form projection is conservative. Complex unions, tuple arrays, pattern dictionaries, nested `id` scopes and unsupported shapes use raw JSON; recursive form expansion stops at depth 24 (schema flattening at 64). These display limits do not remove data or bypass original-schema validation. Full-schema null validation is used for presence choices; defaults are never injected, even by explicit create/reset (which creates an empty type-appropriate value).

Enum labels prefer `x-enumDisplayNames`, then `x-enumNames`, then the JSON value. Only annotation arrays aligned in length with the original enum are used; labels remain aligned when null or invalid entries are filtered. Labels never replace stored enum values or weaken validation. Integer schemas with `x-enumFlags` but no enum use the ordinary integer input; an actual enum still rejects undeclared flag combinations. No flags multi-select is provided.

Verification uses pure Node tests and Angular template/type compilation. Browser focus, visual layout and runtime interaction tests are not included.
