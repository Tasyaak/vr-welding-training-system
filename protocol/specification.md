# Quest ↔ ESP32 protocol

Status: **draft v0**. This document defines semantics before a wire encoding and
transport are selected. Do not ship independently implemented endpoints until
golden message vectors are added here.

## Common rules

- Integer fields are unsigned unless stated otherwise.
- `seq` increments per transmitted message and wraps at `uint32` maximum.
- `timestamp_ms` is milliseconds from the sender's monotonic boot clock.
- Receivers ignore unknown fields for forward compatibility.
- Every encoded message must carry `protocol_version`, `type`, `seq`, and
  `timestamp_ms`.
- After three missed heartbeat intervals, ESP32 must stop haptic output.
- Haptic output must also stop on disconnect, malformed command, watchdog
  expiry, or local fault.

Transport, framing, endianness, integrity checks, maximum message size, retry
behavior, and heartbeat interval are unresolved design decisions for v0.

## ESP32 → Quest

### `TELEMETRY`

| Field | Type | Meaning |
| --- | --- | --- |
| `protocol_version` | `uint8` | `0` for this draft |
| `type` | enum | `TELEMETRY` |
| `seq` | `uint32` | Sender sequence number |
| `timestamp_ms` | `uint32` | ESP32 monotonic timestamp |
| `hall_raw` | `int32` | Raw ADC/sensor value |
| `hall_normalized` | `float32` | Calibrated value in `[0, 1]` |
| `status` | `uint8` bitset | Device status and fault flags |

Reserved status bits must be transmitted as zero. Bit assignments will be
defined alongside the first firmware implementation.

## Quest → ESP32

### `HEARTBEAT`

Keeps the command channel alive. It carries only the common fields.

### `HAPTIC_COMMAND`

| Field | Type | Meaning |
| --- | --- | --- |
| common fields | — | Version, type, sequence, timestamp |
| `amplitude` | `float32` | Requested normalized output in `[0, 1]` |
| `duration_ms` | `uint16` | Bounded output duration; never indefinite |

ESP32 clamps amplitude and duration to its configured safe limits.

### `HAPTIC_STOP`

Stops output immediately. It carries only the common fields and is idempotent.

## Verification required before v1

- Select transport and wire encoding.
- Define connection establishment and compatibility negotiation.
- Define status bits, limits, watchdog timeout, and heartbeat period.
- Add golden encoded messages consumed by both Unity and firmware tests.
- Test sequence wrap, loss, duplication, malformed input, reconnect, and stop.
