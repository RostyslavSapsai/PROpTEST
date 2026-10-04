# v0.10 / PT:2.2 — throttle, chart navigation, thrust anomaly stop

v0.11 amendment: UI calls the feature «Аварійна зупинка», new absolute default is 50 g (relative default remains 30%). Wheel events less than 500 ms apart share one undo entry. Time fields hide spinner buttons and use grouped spacing. Firmware and existing recorded settings are unchanged. Historical v0.10 details follow.

## Sound correction

The linear ADC * 120 / 1023 mapping is removed from new PT:2.x measurements. SoundAdc remains measured ADC amplitude; SoundDb is null. Old JSON/SQLite records and explicit PT:1.0 compatibility retain their historical values and dB* label, not calibrated SPL. Simulation uses explicitly synthetic ADC amplitude. No minimum room-noise offset is invented. Microphone identification, gain, frequency response and acoustic reference calibration remain unresolved.

## Fractional throttle

PT:2.2 introduces U:n, n in tenths of one percentage point. U:61 means 6.1%, nominal PWM 1061 us. W:n retains integer percent compatibility. M:n remains integer percent ceiling, internally multiplied by ten. Target/applied/ceiling use uint16_t; all requests remain bounded by 300 tenths (30%). Telemetry applied_pct uses one decimal. Slew is one tenth per at least 10 ms; nominal speed remains 10 percentage points/s, with processing/Servo timing affecting physical output. Zero is immediate at the command level. PWM timing accuracy and rotor response were not instrumented.

Desktop sends U only after exact PT:2.2 identity. PT:2.0/2.1/1.0 reject fractional requests with an explanation instead of silently rounding. The 6% shortcut preserves the true command scale; it is based on user-observed startup, not a calibrated minimum speed or automatic deadband remapping. A lower configured ceiling disables the shortcut.

## Thrust guard

Host-side guard is enabled by default in new UI runs; old archived settings deserialize disabled. Settings are frozen for a running test and saved in its RunSettings JSON. Default thresholds 100 g and 30% are configurable initial choices, not validated mechanical limits.

Any requested/applied throttle change, missing thrust or a sample gap greater than max(1000 ms, 2 * configured interval) resets the baseline. Applied throttle must match requested within 0.05 percentage points. Wait 1500 ms, then collect at least 3 valid measurements. Compare each new force with the mean of up to 5 preceding accepted measurements. Threshold is max(absolute grams, abs(mean) * percentage / 100). Two consecutive absolute deviations at or above this threshold trigger RunController.End, device Stop, Faulted state and persisted ThrustAnomaly outcome. An isolated excursion does not enter the baseline. Reconnection is required before restarting.

This catches positive or negative abrupt changes, not every failure. It cannot establish the root cause, replace independent power interruption, detect all gradual drift, or guarantee physical stopping time. A missing force channel currently resets the guard rather than identifying a mechanical failure. Detection requires two telemetry samples after baseline acquisition: slower sample settings increase latency (up to roughly 4 s for two intervals at 2000 ms), plus host/serial delays. USB/host failure relies on the existing firmware heartbeat timeout. No induced physical anomaly test was performed.

## Chart navigation

Manual view history retains 50 entries, including auto state. Ctrl+Z after chart interaction undoes that chart's range; after the range bar Apply button it undoes the affected group. Text input keeps normal editing behavior. New runs clear prior-run view history. Explicit ranges accept 0–7200 s and a minimum width of 0.05 s, including future time. They do not change test duration, filter storage, interpolate missing samples or resume/stop recording.

## Verification

- Release build: zero warnings/errors; 44 cases passed with both capture and COM5 zero-output checks explicitly enabled.
- Cases cover fine command encoding, old firmware rejection, preserved ADC/null dB, range input, Ctrl+Z pointer navigation, numeric throttle binding, stable/ramping/missing/disabled guard behavior, and controller Stop plus SQLite outcome persistence.
- Firmware compiled with Arduino AVR 1.8.8 / Servo 1.3.0: 12138 bytes flash, 676 bytes global RAM. Uploaded successfully to COM5.
- Real zero-output recording: 7 measurements; all applied throttle 0; all four channels available, SoundDb null. Stop acknowledged and archive sample count matched.
- UI rendered at 1220×850 and 960×680 and as separate chart; visual score 9/10 after 2 iterations. Narrow layout requires sidebar scrolling for lower settings. Native Windows DPI was not inspected.
- Firmware output under load, physical anomaly response, acoustic SPL and calibration accuracy remain unverified in this revision.
