# ESP32 firmware

This directory will contain the Arduino-compatible ESP32 firmware that sends
telemetry to Quest and receives haptic commands.

Before implementation, choose and document the physical board, transport, pin
mapping, haptic driver, sensor electrical limits, and watchdog behavior. The
wire contract is defined in [`../protocol/specification.md`](../protocol/specification.md).

Never commit Wi-Fi credentials or station-specific addresses. Put local values
in `secrets.h` or `config.local.h`; both are ignored by Git.
