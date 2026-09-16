# Protocol v0

ESP32 → Quest

TELEMETRY
seq: uint32
timestamp_ms: uint32
hall_raw: int
hall_normalized: float
status: uint8

Quest → ESP32

HEARTBEAT
HAPTIC_COMMAND
HAPTIC_STOP