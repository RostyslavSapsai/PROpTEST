
> Актуалізація v0.7: припущення цього документа щодо контактів і MPU6050 спростовані зчитаною старою прошивкою та живими відповідями датчиків. Поточні факти: [hardware-v07.md](hardware-v07.md). Прошивку PT:2.0 завантажено; проведено коротку пробу до 15%.
# Hardware evidence — 2026-09-09

## Confirmed by supplied photographs

- Keyestudio UNO with ATmega328P (Uno R3-compatible).
- Hobbywing X-Rotor 40A label: 2–6S LiPo, No BEC.
- Motor marked 2807, 1300 KV. Manufacturer and rated operating envelope not identified.

## Reported by user

- Propeller 7X4X3R.
- Keyestudio ACS712 current module, believed to be ±20 A; exact variant should be checked before calibration.
- Battery voltage observed as 16.44 V; chemistry, wiring, capacity and discharge limits are not confirmed.
- Per-cell voltage is viewed on a separate charging device; no voltage channel reaches Arduino.
- Thrust was compared against laboratory scales; current reportedly checked. Raw reference results unavailable.
- Sensor Shield marked V5.2. Microphone model not identified.

## Prototype source configuration, not independently verified wiring

| Function | Uno pin |
|---|---|
| ESC command | D9 |
| HX711 DOUT / SCK | D4 / D5 |
| Current | A0 |
| Sound | A1 |
| MPU6050 I2C SDA / SCL | A4 / A5 |

Do not treat 40 A ESC labeling as a motor/current-sensor limit or an active protection circuit.
Do not derive safe loaded operation from KV alone. No hardware commands have been issued by this version.
The prior desktop inspection found only Bluetooth COM3/COM4 at that moment; that is not a permanent device inventory.

