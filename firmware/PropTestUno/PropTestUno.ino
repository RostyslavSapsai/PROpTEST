#include <Servo.h>
#include <Wire.h>
#include <avr/wdt.h>
#include <util/atomic.h>
#include "SoundEnvelope.h"

// Stand wiring recovered from original flash, then confirmed by live sensor reads.
constexpr byte ESC_PIN = 9, HX_DATA = 6, HX_CLOCK = 7;
constexpr byte HARD_LIMIT = 30;
constexpr unsigned long LEASE_MS = 1000;
Servo esc;
char command[40];
byte used = 0;
bool overflow = false, armed = false, mpuReady = false, hxReady = false, currentReady = false;
byte mpuAddress = 0x68;
// Throttle in tenths of a percent; 1 unit corresponds to 1 microsecond.
uint16_t target = 0, applied = 0, ceiling = HARD_LIMIT * 10;
bool isMma8452 = false;
unsigned int intervalMs = 200;
unsigned long lastHeartbeat, lastSample, lastSlew, lastHx, sampleSequence;
unsigned long lastCurrent;
unsigned long lastMpuProbe;
float currentAdc, currentAmps;
long hxRaw;
long tareWindow[16], tareSum = 0;
byte tareCount = 0, tareIndex = 0;
// Recovered reciprocal scale bits 0x3abf1061 from the original board flash.
// Historical calibration, not a new reference-weight validation.
constexpr float HX_GRAMS_PER_COUNT = 0.0014577024849131703f;
float ax, ay, az;
SoundEnvelope sound;
unsigned long lastMicUs;

void readMicrophone() {
  if (micros() - lastMicUs < 500) return;
  lastMicUs = micros();
  // Discard after channel switching from A3 to allow ADC input settling.
  analogRead(A0);
  sound.add(analogRead(A0), millis());
}

void output(uint16_t tenths) {
  applied = min(tenths, (uint16_t)(HARD_LIMIT * 10));
  esc.writeMicroseconds(1000 + applied);
}
void stopMotor() { armed = false; target = 0; output(0); }
bool writeRegister(byte reg, byte value) {
  Wire.beginTransmission(mpuAddress); Wire.write(reg); Wire.write(value);
  return Wire.endTransmission() == 0;
}
bool readRegisters(byte reg, byte count) {
  Wire.beginTransmission(mpuAddress); Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  return Wire.requestFrom(mpuAddress, count) == count;
}
void initializeMpu() {
  for (byte addr = 0x1c; addr <= 0x1d; addr++) {
    mpuAddress = addr;
    if (!readRegisters(0x0d, 1) || Wire.read() != 0x2a) continue;
    isMma8452 = true;
    // MMA8452Q: +/-8 g after observed +/-2 g clipping during the 15% probe.
    // 50 Hz output, active. No high-pass (gravity retained).
    mpuReady = writeRegister(0x2a, 0) && writeRegister(0x0e, 2) && writeRegister(0x2a, 0x21);
    return;
  }
  for (byte addr = 0x68; addr <= 0x69; addr++) {
    mpuAddress = addr;
    if (!readRegisters(0x75, 1)) continue;
    if ((Wire.read() & 0x7e) != 0x68) continue;
    // +/-2 g, approximately 5 Hz accelerometer low-pass; this is trend telemetry.
    mpuReady = writeRegister(0x6b, 0) && writeRegister(0x1c, 0) && writeRegister(0x1a, 6);
    return;
  }
}
void readHx() {
  if (digitalRead(HX_DATA)) return;
  unsigned long value = 0;
  // Keep each high pulse below the HX711 power-down threshold even during Servo IRQs.
  for (byte i = 0; i < 25; i++) {
    ATOMIC_BLOCK(ATOMIC_RESTORESTATE) {
      digitalWrite(HX_CLOCK, HIGH); delayMicroseconds(1);
      if (i < 24) value = (value << 1) | digitalRead(HX_DATA);
      digitalWrite(HX_CLOCK, LOW);
    }
    delayMicroseconds(1);
  }
  hxRaw = (long)(value ^ 0x800000UL) - 0x800000L;
  hxReady = hxRaw != 8388607L && hxRaw != -8388608L;
  lastHx = millis();
  if (!armed && hxReady) {
    if (tareCount == 16) tareSum -= tareWindow[tareIndex]; else tareCount++;
    tareWindow[tareIndex] = hxRaw; tareSum += hxRaw; tareIndex = (tareIndex + 1) % 16;
  }
}
void readCurrent() {
  analogRead(A3);
  unsigned long sum = 0;
  for (byte i = 0; i < 16; i++) sum += analogRead(A3);
  currentAdc = sum / 16.0f;
  currentAmps = (currentAdc - 511.5f) * (5.0f / 1023.0f) / 0.100f;
  currentReady = currentAdc > 5 && currentAdc < 1018;
  if (target > 0 && (!currentReady || fabs(currentAmps) >= 8.0f)) {
    stopMotor(); Serial.println(F("FAULT:CURRENT"));
  }
}
int16_t readWord() {
  byte high = Wire.read();
  byte low = Wire.read();
  return (int16_t)(((uint16_t)high << 8) | low);
}
void sample() {
  bool accelValid = mpuReady && readRegisters(isMma8452 ? 0x01 : 0x3b, 6);
  if (accelValid) {
    int16_t x = readWord();
    int16_t y = readWord();
    int16_t z = readWord();
    // MMA's +/-8 g signed 12-bit samples are left-aligned: (raw / 16) / 256 g.
    float scale = isMma8452 ? 4096.0f : 16384.0f;
    ax = x / scale; ay = y / scale; az = z / scale;
  }
  // Nominal ACS712-20A transfer, NOT calibrated. Retain signed readings.
  Serial.print(F("D|")); Serial.print(++sampleSequence); Serial.print('|'); Serial.print(millis());
  Serial.print('|'); Serial.print(applied / 10.0f, 1); Serial.print('|');
  if (hxReady && millis() - lastHx <= 500) Serial.print(hxRaw);
  Serial.print('|'); if (currentReady) Serial.print(currentAmps, 3);
  Serial.print('|'); if (accelValid) Serial.print(ax, 4);
  Serial.print('|'); if (accelValid) Serial.print(ay, 4);
  Serial.print('|'); if (accelValid) Serial.print(az, 4);
  Serial.print('|'); if (sound.windows) Serial.print(sound.mean(), 2);
  sound.consume();
  Serial.print('|'); Serial.print(armed ? 1 : 0);
  Serial.print('|');
  if (hxReady && millis() - lastHx <= 500 && tareCount == 16)
    Serial.print((hxRaw - tareSum / 16.0f) * HX_GRAMS_PER_COUNT, 3);
  Serial.println();
}
void processCommand() {
  if (!strcmp(command, "P")) { Serial.println(F("PT:2.2")); return; }
  if (!strcmp(command, "G")) { stopMotor(); Serial.println(F("ACK:G:0")); return; }
  if (!strcmp(command, "H")) { if (armed) lastHeartbeat = millis(); return; }
  if (!strcmp(command, "B")) {
    stopMotor();
    if (millis() < 3000) { Serial.println(F("ERR:STARTUP")); return; }
    armed = true; lastHeartbeat = millis(); sampleSequence = 0;
    lastSample = millis(); sound.reset(millis());
    Serial.println(F("ACK:B:0")); return;
  }
  if (!strcmp(command, "Q")) { sample(); return; }
  if (!strcmp(command, "A")) {
    Serial.print(F("SOUND|")); Serial.print(sound.lastLow); Serial.print('|');
    Serial.print(sound.lastHigh); Serial.print('|'); Serial.println(sound.lastCount); return;
  }
  if (!strcmp(command, "I")) {
    Serial.print(F("INFO|A3=")); Serial.print(analogRead(A3));
    Serial.print(F("|HX_D6=")); Serial.print(digitalRead(HX_DATA));
    Serial.print(F("|ACC_ADDR=")); Serial.println(mpuReady ? mpuAddress : 0);
    if (!armed) {
      Serial.print(F("I2C:"));
      for (byte address = 8; address < 120; address++) {
        Wire.beginTransmission(address);
        if (!Wire.endTransmission()) { Serial.print(' '); Serial.print(address, HEX); }
      }
      Serial.println();
      byte previousAddress = mpuAddress;
      mpuAddress = 0x1d;
      Serial.print(F("ID1D:"));
      const byte registers[] = {0x00, 0x0d, 0x0f};
      for (byte reg : registers) {
        Serial.print(' '); Serial.print(reg, HEX); Serial.print('=');
        if (readRegisters(reg, 1)) Serial.print(Wire.read(), HEX); else Serial.print('?');
      }
      mpuAddress = previousAddress;
      Serial.println();
      Serial.print(F("ANALOG:"));
      for (byte pin = A0; pin <= A3; pin++) { analogRead(pin); Serial.print(' '); Serial.print(analogRead(pin)); }
      Serial.println();
    }
    return;
  }
  if (strlen(command) < 3 || command[1] != ':') { Serial.println(F("ERR:COMMAND")); return; }
  char *end;
  long value = strtol(command + 2, &end, 10);
  if (*end || end == command + 2 || value < 0) { Serial.println(F("ERR:VALUE")); return; }
  if (command[0] == 'M' && !armed && value >= 1 && value <= HARD_LIMIT) ceiling = value * 10;
  else if (command[0] == 'F' && !armed && value >= 50 && value <= 2000) intervalMs = value;
  else if (armed && ((command[0] == 'U' && value <= ceiling) || (command[0] == 'W' && value <= ceiling / 10))) {
    if (value > 0 && (!currentReady || fabs(currentAmps) >= 8.0f)) {
      stopMotor(); Serial.println(F("FAULT:CURRENT")); return;
    }
    target = command[0] == 'U' ? value : value * 10;
    if (target == 0) output(0);
  } else { Serial.println(F("ERR:LIMIT_OR_STATE")); return; }
}
void setup() {
  MCUSR = 0; wdt_disable();
  esc.attach(ESC_PIN, 1000, 2000); stopMotor();
  pinMode(HX_DATA, INPUT_PULLUP); pinMode(HX_CLOCK, OUTPUT); digitalWrite(HX_CLOCK, LOW);
  Serial.begin(115200);
  Wire.begin(); Wire.setWireTimeout(3000, true);
  wdt_enable(WDTO_1S);
  initializeMpu();
  sound.reset(millis());
  Serial.println(F("PT:2.2"));
}
void loop() {
  wdt_reset();
  if (armed && millis() - lastHeartbeat > LEASE_MS) {
    stopMotor(); Serial.println(F("FAULT:HEARTBEAT"));
  }
  // Bound work per loop, including malformed/overlong input.
  for (byte n = 0; n < 32 && Serial.available(); n++) {
    char c = Serial.read();
    if (c == '\r') continue;
    if (c == '\n') {
      command[used] = 0;
      if (!overflow) processCommand();
      else { stopMotor(); Serial.println(F("FAULT:INPUT")); }
      used = 0; overflow = false;
    } else if (used < sizeof(command) - 1) command[used++] = c;
    else overflow = true;
  }
  if (armed && millis() - lastSlew >= 10) {
    lastSlew = millis();
    if (applied < target) output(applied + 1);
    else if (applied > target) output(applied - 1);
  }
  readHx();
  readMicrophone();
  if (!mpuReady && !armed && millis() - lastMpuProbe >= 1000) {
    lastMpuProbe = millis(); initializeMpu();
  }
  if (millis() - lastCurrent >= 50) { lastCurrent = millis(); readCurrent(); }
  if (armed && millis() - lastSample >= intervalMs) { lastSample = millis(); sample(); }
}


