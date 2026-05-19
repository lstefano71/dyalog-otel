# Bundled Message Template Syntax

Log signal calls now bundle the message template and its positional filler values together as a single nested APL vector, rather than separating the template string from its fillers across two arguments. This eliminates the dual-mode ambiguity on the `attrs` argument (which was previously either fillers or key-value pairs depending on whether the message contained `{Placeholders}`) and enables mixed usage: template fillers + extra key-value attributes on the same call.

## Considered Options

**Filler location:**
- Bundled in message arg (chosen) — `('Order {OrderId} placed for {Amount}' 1001 59.99)`. Fillers are visually adjacent to the template they fill. The `attrs` arg is freed to always mean key-value pairs.
- Separate attrs arg (previous) — fillers in position 6, far from the template. `attrs` had dual interpretation (fillers when template has placeholders, key-value pairs otherwise). Mixed mode was impossible.

**Discriminator:**
- `msg.IsNested()` (chosen) — natural APL idiom. A nested vector means template+fillers; a simple char vector means plain message. No magic markers or flags needed.

**Backward compatibility:**
- Clean break (chosen) — no live setups exist outside this repo. All `.apls` files updated. No dual-syntax support period.
- Support both old and new — rejected; adds dead code paths and confusing docs for zero users.

## Consequences

- `attrs` (6th argument) is now always flat key-value pairs: `('key1' val1 'key2' val2)`. No more implicit filler interpretation.
- Template parsing only activates when `msg.IsNested()`. A flat message with `{Braces}` is literal text.
- On filler count mismatch: best-effort, silent (unresolved placeholders stay as literal text, extras ignored).
- Template-derived attributes take precedence over `attrs` on name collision.
- Same rule applies uniformly to `pp_otel_log` and `pp_otel_log_span`.
