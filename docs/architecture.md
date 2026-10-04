# Current architecture — v0.10

Current changes are specified in controls-v10.md: PT:2.2 adds explicit tenths-percent command U:n; new PT:2.x SoundDb is null and SoundAdc holds measured amplitude. Core ThrustGuard runs within RunController using persisted per-run settings and stops through the existing device boundary. Desktop chart view ranges/history remain independent of sample storage. Legacy archival values are not rewritten. These changes supersede sound/throttle descriptions below.

## Historical v0.7/v0.8 updates

v0.8 supersedes the polarity and sound presentation below: PT:2.0 normalization applies thrust polarity −1 (user-confirmed direction) and SoundDb = ADC × 120 / 1023, explicitly uncalibrated. Raw channels are retained and Source records the transformations. Existing archives are not migrated. Slider maximum and tick spacing bind to the enabled limit. Firmware is unchanged.

PT:2.0 is now flashed and supported alongside explicit PT:1.0 compatibility. See ../firmware/README.md for the wire protocol and hardware-v07.md for evidence. PT:2.0 uses direct percent commands, acknowledged Begin/Stop, local heartbeat expiry and a hard 30% ceiling. Internal telemetry adds optional raw HX/sound fields (14 fields total); 8/12-field archives remain readable. Thrust/current can now be null. Desktop shows unavailable channels as gaps, raw sound as ADC. Stored grams use a recovered historical coefficient and require reference validation. Existing host receipt timestamps are retained; wire uptime is validated but is not used as a synchronized clock.

## Previous architecture snapshot (v0.6; superseded where noted above)

# Architecture — v0.6

Core owns session state, run settings, telemetry framing/validation and repository/device interfaces.
Infrastructure provides SQLite persistence and the PT:1.0 serial compatibility adapter.
Desktop selects COM (default) or explicit SIM, renders four channels, records and exports runs.

RunController serializes run operations. Connection runs on a background task outside its lock;
the UI remains responsive during the 7-second identity probe. A 50 ms worker polls independently
of the 200 ms UI refresh. Serial reads use ReadExisting; writes have a 200 ms timeout.
These bounded writes can still delay Stop while another controller operation holds the lock.
Production instances share one mutex to protect recovery. Smoke tests use a separate database and mutex.

## COM compatibility

115200 8N1, newline commands, DTR/RTS false. USB inventory is read-only. Production desktop probes Windows Arduino VID_2341/VID_2A03 ports once per attachment. Bluetooth and generic USB-UART are not automatically opened. Tests and smoke launches disable live auto connection.
P -> exact PT:1.0 is required before G or any motor configuration. No automatic legacy fallback.
Opening a serial port may reset Uno even with DTR false; startup/calibration behavior needs physical verification.
Begin sends G, clears buffered input, sets M:<limit> and F:<interval>, then W:0.
Firmware maps floor(W * M / 100), so the adapter sends ceil(requested_integer_percent * 100 / M).
The device-side ceiling remains configured. At zero, periodic W:0 requests obtain samples because the firmware
only continuously streams at nonzero throttle. Nonzero settings are sent on slider changes.

Seven fields: thrust | accX | accY | accZ | current | uncalibratedSound | appliedPercent.
Only finite seven-field samples are accepted. Six-field legacy frames cannot confirm throttle and are rejected.
The adapter assigns run ID, sequence and host receipt time. PT:1.0 has no measurement timestamp or sequence:
we cannot detect all dropped/repeated measurements or guarantee exclusion of an old frame still in flight after clearing input.
Host timeout max(2000 ms, 3 configured sample intervals) faults the run and attempts G.
No device watchdog, command ACK, reliable remote-disconnect detection while idle, or independent current protection exists.
A host Stop is a command attempt, not confirmation that the rotor stopped.

## Telemetry and storage

Internal frame: PT2|S|guid|seq|elapsedMs|throttle|thrust|current|accX|accY|accZ|sound.
Eight-field old frames remain readable; four new optional numeric fields are empty when unavailable.
Maximum 512 characters, invariant culture, finite values, monotonic sequence and nondecreasing time.
This is an internal normalization format, not new firmware deployed on the device.
Existing SQLite v1 JSON payloads remain compatible through nullable added fields. Existing data are not recalibrated.
Source metadata and channel values are preserved in CSV. Missing channels are blank.

SIM remains explicit and synthetic. Its slew is 20 percentage points/s and lease is 750 ms.
It has no independent hardware execution: simulation time advances during Poll. Its success says nothing about a physical watchdog.

Charts are presentation-only: auto shows the complete run from time zero and expands with incoming measurements; manual time navigation freezes the chosen interval. Binary search locates the beginning of a selected range. Left-drag selects a time interval, right-drag pans, Ctrl+wheel zooms (ordinary wheel bubbles to the scroll viewer); Y scales to visible values. Detached live charts subscribe to data updates and unsubscribe on close; archive windows retain an immutable record snapshot. Tooltips remain constrained to visible points. No changes were made to device control or telemetry storage in v0.3.
SQLite currently opens pooled connections per sample. Full run snapshots are copied at 5 Hz;
two-hour throughput, memory use, Linux, real DPI and actual hardware remain unverified.

## Next reliable hardware version

1. Implement and test Uno local watchdog, bounded input, explicit arming and acknowledged Stop.
2. Define timestamped sensor-validity telemetry and device capabilities; keep this compatibility adapter separately labelled.
3. Validate actual wiring, sensor conversions, calibration and ESC output without a propeller.
4. Verify stop/reset/unplugging and measured limits before loaded runs.
5. Add ramps/stabilization; battery tests need voltage acquisition and battery limits.

Keep interfaces for concrete extensions; do not add speculative plugin or generic workflow infrastructure.


Errors from UI operations and transitions to Faulted are queued in the view model and shown in a single nonmodal owned window. The error window has only a dismiss button and deduplicates currently displayed messages; operational windows retain Stop. Known error messages receive a specific title and actionable guidance; unknown causes are not inferred, and raw messages remain available in expandable details. Routine status remains in the footer. Summary vibration is the XYZ acceleration norm, including gravity; it is not an RMS vibration metric.



AutoConnectionTracker remembers attempts until an observed removal. A 1-second background catalog check updates the UI and triggers async handshake only while disconnected. Manual disconnect suppresses attempts until reattachment. USB discovery is separate from protocol readiness; no recording or nonzero output starts automatically. Fast unplug/replug between checks can be missed; manual retry remains available.

