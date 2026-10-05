#pragma once
#include <Arduino.h>
#include <esp_task_wdt.h>

// Permanent outputs from wiring-v2. GPIO14 is no longer driven.
constexpr uint8_t MOTOR_PINS[4] = {10, 11, 12, 13};
portMUX_TYPE motorMux = portMUX_INITIALIZER_UNLOCKED;
struct MotorState {
  bool armed = false, ready = false;
  uint16_t target[4] = {}, applied[4] = {};
  uint32_t started = 0, heartbeat = 0, tick = 0, token = 0;
  const char *reason = "STOP";
} motor;

MotorState motorSnapshot() {
  portENTER_CRITICAL(&motorMux); auto copy = motor; portEXIT_CRITICAL(&motorMux);
  return copy;
}
void motorStop(const char *reason) {
  portENTER_CRITICAL(&motorMux);
  motor.armed = false; for (auto &value : motor.target) value = 0; motor.token = 0; motor.reason = reason;
  portEXIT_CRITICAL(&motorMux);
}
void motorTask(void *) {
  esp_task_wdt_add(nullptr);
  for (;;) {
    uint32_t now = millis();
    portENTER_CRITICAL(&motorMux);
    if (motor.armed && (uint32_t)(now - motor.heartbeat) >= 1000) {
      motor.armed = false; motor.reason = "LINK_LOST";
    }
    if (motor.armed && (uint32_t)(now - motor.started) >= 30000) {
      motor.armed = false; motor.reason = "TIME_LIMIT";
    }
    if (!motor.armed) { for (auto &value : motor.target) value = 0; motor.token = 0; }
    uint16_t next[4];
    for (int i = 0; i < 4; i++) {
      next[i] = !motor.target[i] ? 0 : motor.applied[i] < motor.target[i] ? motor.applied[i] + 1
          : motor.applied[i] > motor.target[i] ? motor.applied[i] - 1 : motor.applied[i];
      next[i] = min((uint16_t)100, next[i]);
    }
    portEXIT_CRITICAL(&motorMux);
    bool ok = true;
    for (int i = 0; i < 4; i++)
      ok &= ledcWrite(MOTOR_PINS[i], ((1000UL + next[i]) * 16384UL + 10000UL) / 20000UL);
    if (!ok) for (auto pin : MOTOR_PINS) { ledcDetach(pin); pinMode(pin, OUTPUT); digitalWrite(pin, LOW); }
    portENTER_CRITICAL(&motorMux);
    for (int i = 0; i < 4; i++) motor.applied[i] = ok ? next[i] : 0;
    motor.tick = now;
    if (!ok) { motor.ready = false; motor.armed = false; motor.reason = "PWM_FAILED"; }
    portEXIT_CRITICAL(&motorMux);
    esp_task_wdt_reset();
    vTaskDelay(pdMS_TO_TICKS(20));
  }
}
bool motorBegin() {
  for (auto pin : MOTOR_PINS) {
    if (!ledcAttach(pin, 50, 14) || !ledcWrite(pin, (1000UL * 16384UL + 10000UL) / 20000UL)) {
      for (auto output : MOTOR_PINS) { ledcDetach(output); pinMode(output, OUTPUT); digitalWrite(output, LOW); }
      return false;
    }
  }
  esp_task_wdt_config_t config = { .timeout_ms = 2000, .idle_core_mask = 0, .trigger_panic = true };
  esp_err_t result = esp_task_wdt_init(&config);
  if (result == ESP_ERR_INVALID_STATE) result = esp_task_wdt_reconfigure(&config);
  if (result != ESP_OK) return false;
  motor.ready = xTaskCreate(motorTask, "motor-stop", 3072, nullptr, 3, nullptr) == pdPASS;
  return motor.ready;
}
bool motorOutputsZero(const MotorState &s) {
  for (int i = 0; i < 4; i++) if (s.applied[i] || s.target[i]) return false;
  return true;
}
bool motorStopped() {
  uint32_t requestedAt = millis();
  motorStop("STOP");
  for (int n = 0; n < 20; n++) {
    auto s = motorSnapshot();
    // Wait for a fresh task cycle, not an old zero observed before the stop request.
    if (s.ready && motorOutputsZero(s) && (int32_t)(s.tick - requestedAt) > 0 && (uint32_t)(millis() - s.tick) < 100) return true;
    delay(5);
  }
  return false;
}
