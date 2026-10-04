#pragma once
#include <Arduino.h>
#include <esp_task_wdt.h>

// Temporary old Hobbywing PWM output. Permanent ESC outputs 10..13 are untouched.
constexpr uint8_t MOTOR_PIN = 14;
portMUX_TYPE motorMux = portMUX_INITIALIZER_UNLOCKED;
struct MotorState {
  bool armed = false, ready = false;
  uint16_t target = 0, applied = 0;
  uint32_t started = 0, heartbeat = 0, tick = 0, token = 0;
  const char *reason = "STOP";
} motor;

MotorState motorSnapshot() {
  portENTER_CRITICAL(&motorMux); auto copy = motor; portEXIT_CRITICAL(&motorMux);
  return copy;
}
void motorStop(const char *reason) {
  portENTER_CRITICAL(&motorMux);
  motor.armed = false; motor.target = 0; motor.token = 0; motor.reason = reason;
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
    if (!motor.armed) { motor.target = 0; motor.token = 0; }
    uint16_t next = !motor.target ? 0 : motor.applied < motor.target ? motor.applied + 1
        : motor.applied > motor.target ? motor.applied - 1 : motor.applied;
    next = min((uint16_t)100, next);
    portEXIT_CRITICAL(&motorMux);
    // 50 Hz / 14 bits: 1.22 us resolution. Zero is a 1000 us stop pulse.
    bool ok = ledcWrite(MOTOR_PIN, ((1000UL + next) * 16384UL + 10000UL) / 20000UL);
    if (!ok) { ledcDetach(MOTOR_PIN); pinMode(MOTOR_PIN, OUTPUT); digitalWrite(MOTOR_PIN, LOW); }
    portENTER_CRITICAL(&motorMux);
    motor.applied = ok ? next : 0; motor.tick = now;
    if (!ok) { motor.ready = false; motor.armed = false; motor.reason = "PWM_FAILED"; }
    portEXIT_CRITICAL(&motorMux);
    esp_task_wdt_reset();
    vTaskDelay(pdMS_TO_TICKS(20));
  }
}
bool motorBegin() {
  if (!ledcAttach(MOTOR_PIN, 50, 14)) return false;
  if (!ledcWrite(MOTOR_PIN, (1000UL * 16384UL + 10000UL) / 20000UL)) return false;
  esp_task_wdt_config_t config = { .timeout_ms = 2000, .idle_core_mask = 0, .trigger_panic = true };
  esp_err_t result = esp_task_wdt_init(&config);
  if (result == ESP_ERR_INVALID_STATE) result = esp_task_wdt_reconfigure(&config);
  if (result != ESP_OK) return false;
  motor.ready = xTaskCreate(motorTask, "motor-stop", 3072, nullptr, 3, nullptr) == pdPASS;
  return motor.ready;
}
bool motorStopped() {
  uint32_t requestedAt = millis();
  motorStop("STOP");
  for (int n = 0; n < 20; n++) {
    auto s = motorSnapshot();
    // Wait for a fresh task cycle, not an old zero observed before the stop request.
    if (s.ready && s.applied == 0 && (int32_t)(s.tick - requestedAt) > 0 && (uint32_t)(millis() - s.tick) < 100) return true;
    delay(5);
  }
  return false;
}
