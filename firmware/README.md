# PROpTEST Uno PT:2.2

PT:2.1 supersedes the instantaneous microphone acquisition described below: sound_adc now holds the mean peak-to-peak ADC amplitude of complete 50 ms windows. Nominal acquisition interval 500 µs, actual timing is interrupted by other loop tasks. Missing windows yield an empty field, constant input yields zero. See ../docs/sound-v09.md. Current identity is PT:2.2; diagnostic A returns SOUND|last_min|last_max|last_sample_count. Control commands/pin mapping remain compatible with PT:2.0. The desktop must recognize the new identity and store its source semantics. PT:2.2 adds U:n for fractional throttle; see ../docs/controls-v10.md. Sound remains ADC amplitude, not SPL.

Board profile: ATmega328P Uno, Servo 1.3.0, Arduino AVR 1.8.8. Wiring is recovered from the original flash (see docs/hardware-v07.md), not from the obsolete on-disk prototype sketch. Original EEPROM is not written.

115200, 8N1, LF; maximum command 39 characters, overlong line disarms. Commands are case-sensitive.

| Command | Behavior |
|---|---|
| P | PT:2.2 identity; no arm |
| G | Disarm, PWM 1000 µs, ACK:G:0 |
| M:n | Set ceiling 1–30, only disarmed |
| F:n | Telemetry interval 50–2000 ms, only disarmed |
| B | Explicit arm at zero after 3 s uptime, ACK:B:0 |
| U:n | Direct tenths of a percent, 0 to 10×ceiling, only armed; same current checks as W |
| W:n | Direct requested percent 0–ceiling, only armed; nonzero requires usable nominal current <8 A magnitude |
| H | Renew armed heartbeat lease; never re-arms |
| Q | One diagnostic sample, including when disarmed |
| I | Pin/ADC/device diagnostic; I2C scan only when disarmed |

Telemetry: D|sequence|uptime_ms|applied_pct|hx_raw|current_a|x_g|y_g|z_g|sound_adc|armed|historical_thrust_g.
Empty fields mean unavailable. No fabricated zero on missing sensors. Current and grams are provisional conversions, not newly validated calibration. Desktop normalizes run identity and host receipt time and retains raw HX/sound.

Heartbeat expiry after >1000 ms disarms and reports FAULT:HEARTBEAT. Current magnitude >=8 A or ADC rail while target is nonzero disarms and reports FAULT:CURRENT; current is checked every 50 ms independently of the UI sample interval. It is not a hardware-rated safety system. MCU watchdog resets on a loop stall; physical power-loss/rotor stop time remains to be measured.

PWM = 1000 + 10×applied_percent µs. Maximum 1300 µs; no startup high-throttle pulse and no automatic ESC endpoint calibration. Nonzero steps change by 0.1% per at least 10 ms, zero is immediate. No nonzero output on connecting or beginning recording.

HX zero follows a 16-sample rolling average while disarmed and freezes on B. Historical reciprocal coefficient recovered from flash is 0.0014577025. Never auto-tare an already spinning rotor. Raw values and signed force are retained.

MMA8452Q 0x1D confirmed physically; auto-identification also checks 0x1C and MPU6050 0x68/0x69 for the historical profile. MMA range ±8 g, output 50 Hz, no gravity removal. MPU fallback ±2 g with 5 Hz low-pass has not been tested on this hardware. Reporting the latest sample at a slower rate can alias vibration; no RMS or spectrum claim.

Build with the local CLI configuration under artifacts/hardware-diagnostics/arduino; output artifacts/hardware-diagnostics/firmware-build. Upload is a separate explicit hardware operation, not part of the normal desktop build. Original flash/EEPROM backups must be retained.

