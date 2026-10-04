# Sound acquisition fix — v0.9 / PT:2.1

Confirmed source defect: PT:2.0 called analogRead(A0) once per telemetry frame. At the default 200 ms interval that is only five instantaneous observations per second. Multiplying that by 120/1023 did not measure a sound envelope, so transient waveform troughs produced repeated zeros. Original microphone model/gain and acoustic response remain unconfirmed.

PT:2.1 samples A0 opportunistically at a nominal 500 µs interval, discarding an ADC conversion for channel settling. Other tasks cause gaps; this is not uniform full-band audio sampling. Hardware diagnostic A reported about 85–95 samples per 50 ms window during the initial zero-output probe.

For each complete 50 ms window, amplitude = max(ADC) − min(ADC). The outgoing value is the arithmetic mean of valid complete windows since the preceding report. Windows with fewer than 10 samples or a timing gap reaching 100 ms are excluded; no completed windows gives an empty field. Exact constant input still gives zero. No zero deletion, interpolation, or positive floor is applied. B resets the acquisition window to exclude earlier idle data.

The desktop retains the requested provisional indicator dB* = mean_peak_to_peak × 120 / 1023. It is a linear display scale, NOT logarithmic dB SPL. Wire field sound_adc and stored SoundAdc now represent this windowed amplitude for PT:2.1; old PT:2.0 records represent instantaneous ADC. The source metadata distinguishes the versions/metrics. Old archives are unchanged. Updated app accepts PT:2.1/2.0/1.0; older EXEs will not recognize the new identity.

Limits, pins, PWM, motor slew, current guard and watchdog policy are unchanged. The first current conversion after switching ADC channels is now also discarded for settling.

## Physical evidence

Firmware compiled and uploaded to COM5 with the existing Uno toolchain. Short motor probe max 10%:
`artifacts/hardware-diagnostics/probe-10-20260911-222352.log`.

- 0%: 11 samples, mean amplitude 22.15 ADC, zero values 0.
- 10%: 8 samples, mean amplitude 684.78 ADC, zero values 0.
- Probe ended with ACK:G:0. No higher motor command in this turn.
- These are electrical microphone-output observations, not calibrated acoustic measurements; electrical coupling and microphone clipping have not been excluded.

Actual samples were rendered without cosmetic smoothing in `artifacts/verification-v09-final/sound-capture.png`. Main app COM5 zero-output recording, persistence and acknowledged stop passed separately. Desktop Release build/publish and smoke launch passed. 37 ordinary automated cases plus two explicit opt-in hardware/capture checks ran successfully. Visual check: 9/10, one iteration; native DPI not checked.

Remaining: identify microphone module and adjust its physical gain/bias if needed; verify clipping/noise floor, compare against a reference SPL meter, and select an appropriate frequency response before claiming acoustic decibels. Do not discard legitimate zero readings.

Reference: [Arduino analog I/O documentation](https://docs.arduino.cc/language-reference/) and [Keyestudio sound-sensor example](https://docs.keyestudio.com/projects/KT0381/en/latest/doc/Lesson_12_Sound_Sensor.html). The Keyestudio example is general context, not identification of this user's exact module.
