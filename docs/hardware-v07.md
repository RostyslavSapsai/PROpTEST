# Hardware investigation and v0.7 evidence — 2026-09-11

This supersedes the wiring assumptions in hardware.md and the old connection-diagnosis.md conclusion.
Legacy files under D:\PT were not modified. No commit or push performed.

## Root cause

The checked-in legacy sketch and the actual flash were different programs. PT:1.0 was not present in the original flash. Original command dispatch at 0x1c00 onward recognizes `s`, `b`, `g`, not the P identity probe. The new desktop was correctly rejecting its silence, but was built against the wrong source file.

Original flash was backed up before writing:

- `artifacts/hardware-diagnostics/uno-before.bin`, SHA256 DD3DAB54DABF3C53D53541A42E2D4E13A1FFADD928426FF1DD35DBE9D55F323C.
- `uno-eeprom-before.bin`, SHA256 46AA1EFC4C8ED8CF5D5CDDDDEB0A7AA66C3A08CF987E4B08DC5747542330A40D.
- `uno-before.asm`, AVR5 disassembly made with Arduino's avr-objdump.

Evidence in that binary:

| Address | Interpretation | Live corroboration |
|---|---|---|
| 0x1932–0x193a | Register 0x0D compared with 0x2A: MMA8452Q identity | I2C 0x1D responds with 0x2A |
| 0x1a12–0x1a56 | HX data pin 6, clock pin 7; pinMode/input and clock pulses | Continuous HX raw values around 662000 at rest |
| 0x1f84–0x1f86 | analogRead(0), microphone sampling loop | A0 signal changes during motor operation |
| 0x2014–0x2016 | analogRead(17), Uno A3, current conversion loop | A3 about 512 ADC at rest, rises with commanded load |
| 0x1b24, 0x1b44 | Servo pin 9 | Load/current response to PWM command, not oscilloscope verification |
| 0x1a94–0x1aa8; 0x1d60–0x1d70 | Scale bytes 61 10 BF 3A used in thrust conversion | Historical reciprocal ~0.0014577025; reference mass still needed |

The source file's HX D4/D5, ACS A0, MIC A1 and MPU6050 assumptions were therefore wrong for this connected stand. Early diagnostics from the temporary firmware reporting missing sensors reflect those wrong pins; they do not establish broken sensors or an unpowered shield.

## Changes and measured checks

PT:2.0 firmware is uploaded. Uno compiles with Arduino AVR core 1.8.8 and Servo 1.3.0. Uses bounded command input, bounded I2C transactions, nonblocking HX readiness, safe initial output, explicit begin, acknowledged stop, 1 s heartbeat expiry and a 1 s MCU watchdog. Hard output ceiling 30%; rising target slew 1 percentage point / 100 ms. Zero command and Stop are immediate software output changes.

Physical serial checks confirmed identity, ACK:G:0, heartbeat expiry at zero, rejection of throttle after expiry, rejection above the configured ceiling, and current interlock when the wrong analog input was used. Those tests verify firmware responses, not measured pulse width, electrical protection or physical rotor stop time.

`probe-15-20260911-215354.log`: short 0 → 5 → 10 → 15% run, then ACK:G:0. Current at the 15% plateau was about 0.8–0.9 A by the nominal conversion. HX raw decreased to about 586000, historical signed thrust approximately −111 g. This proves correlated sensor response to output commands, not calibrated thrust accuracy. No run to 30% or 100% was performed.

During that run the MMA8452Q clipped repeatedly at +1.999/−2.000 g. Firmware range was then changed to ±8 g (XYZ_DATA_CFG=2), scale 256 counts/g for signed 12-bit samples. Final ±8 g firmware was checked at zero output; its loaded clipping/headroom remains unverified.

`artifacts/verification-v07/hardware-zero.json/png`: actual COM5 recording through the desktop view model, chart rendering, SQLite round trip, and acknowledged Stop. At rest XYZ approximately −0.04 / 1.02 / 0.07 g, consistent with gravity mostly on Y. This is not an accelerometer calibration.

## Remaining work

- Validate thrust scale/sign with known masses, repeatability, drift and mounting direction. Historical scale restored once, not double-divided; signed values intentionally preserved.
- Validate current zero, variant/sensitivity and scale with a reference meter. Nominal current uses (ADC−511.5)×5/1023/0.100 A. The 8 A software interlock is provisional and depends on valid sensing; not a hardware overcurrent guarantee.
- Verify physical rotor stopping, USB cable removal during controlled unloaded operation, reset/brownout behavior, pulse timing and ESC arming independently. No blind full-throttle calibration was performed.
- Minimum reliable motor-start threshold is not yet established. Old binary starts its automatic sequence at 1060 µs, but that is not proof of a universally reliable threshold. No automatic jump to a starting minimum was added.
- MMA8452Q trend samples at 50 Hz and sparse desktop points cannot characterize propeller vibration spectrum/RMS; clipping, mounting resonance and aliasing need dedicated measurements.
- Microphone is raw ADC, not SPL. Noise floor/gain/RMS sampling/acoustic calibration remain unvalidated.
- No voltage sensor: battery duration tests with voltage/cell cutoff, watts and Wh remain out of scope of this change.

## Primary documentation

- [MMA8452Q datasheet](https://www.nxp.com/docs/en/data-sheet/MMA8452Q.pdf): WHO_AM_I, ranges, register layout, output rates.
- [Keyestudio KS0270](https://docs.keyestudio.com/projects/KS0270/en/latest/docs/KS0270.html): matching module and accelerometer operation.
- [ACS712 datasheet](https://www.allegromicro.com/-/media/files/datasheets/acs712-datasheet.pdf): nominal mid-supply zero and 20 A variant sensitivity.
- [Hobbywing XRotor manual](https://www.hobbywingdirect.com/collections/xrotor-user-manual/xrotor): range calibration is a separate procedure, not attempted automatically.
