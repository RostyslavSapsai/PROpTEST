#pragma once
#include <WebServer.h>
#include <Update.h>
#include <esp_ota_ops.h>
#include "MotorCheck.h"
#include "BoardPage.h"

#ifndef PROPTEST_VERSION
#define PROPTEST_VERSION "0.3.0"
#endif
WebServer web(80);
bool pendingBoot = false, updating = false, uploadOk = false;
uint32_t restartAt = 0;
String uploadError;
uint8_t packageHeader[84];
size_t headerCount = 0, imageCount = 0;
uint32_t imageSize = 0;

// Arduino normally validates OTA before setup. Require a working web API instead.
extern "C" bool verifyRollbackLater() { return true; }

bool authorized() {
  if (web.header("X-Proptest") != "1" || !web.authenticate("proptest", password.c_str())) {
    web.send(401, "text/plain; charset=utf-8", "Неправильний пароль плати або запит."); return false;
  }
  web.sendHeader("Cache-Control", "no-store");
  return true;
}
String statusJson(bool includeToken = false) {
  auto s = motorSnapshot();
  String json = "{\"version\":\"S3-Connect " PROPTEST_VERSION "\",\"pin\":14,\"max\":100,\"uptime\":" + String(millis());
  json += ",\"armed\":" + String(s.armed ? "true" : "false") + ",\"ready\":" + String(s.ready ? "true" : "false");
  json += ",\"target\":" + String(s.target) + ",\"applied\":" + String(s.applied);
  json += ",\"reason\":\"" + String(s.reason) + "\",\"pending\":" + String(pendingBoot ? "true" : "false");
  json += ",\"slot\":\"" + String(esp_ota_get_running_partition()->label) + "\"";
  if (includeToken) json += ",\"token\":\"" + String(s.token) + "\"";
  return json + "}";
}
void sendStatus(bool includeToken = false) { web.send(200, "application/json", statusJson(includeToken)); }
void reject(const char *text, int code = 409) { web.send(code, "text/plain; charset=utf-8", text); }
bool sessionValid() {
  auto s = motorSnapshot();
  return s.armed && s.token && web.arg("token") == String(s.token)
      && (uint32_t)(millis() - s.heartbeat) < 1000 && (uint32_t)(millis() - s.started) < 30000;
}
void uploadFail(const char *reason) {
  uploadError = reason; uploadOk = false;
  if (Update.isRunning()) Update.abort();
}
void receiveUpload() {
  auto &u = web.upload();
  if (u.status == UPLOAD_FILE_START) {
    uploadOk = false; uploadError = ""; headerCount = imageCount = imageSize = 0;
    if (web.header("X-Proptest") != "1" || !web.authenticate("proptest", password.c_str())) { uploadError = "Доступ відхилено."; return; }
    if (motorSnapshot().armed || pendingBoot || restartAt) { uploadError = "Спочатку зупиніть тест та підтвердьте запуск прошивки."; return; }
    updating = true;
    if (!motorStopped()) uploadFail("Не підтверджений нульовий вихід.");
  } else if (u.status == UPLOAD_FILE_WRITE && updating && uploadError.isEmpty()) {
    size_t offset = 0;
    while (headerCount < sizeof(packageHeader) && offset < u.currentSize) packageHeader[headerCount++] = u.buf[offset++];
    if (headerCount == sizeof(packageHeader) && !imageSize) {
      if (memcmp(packageHeader, "PROPTEST-S3-OTA1", 16)) { uploadFail("Це не пакет PROpTEST ESP32-S3 (.ptfw)."); return; }
      memcpy(&imageSize, packageHeader + 16, 4);
      auto next = esp_ota_get_next_update_partition(nullptr);
      if (!next || imageSize < 256 || imageSize > next->size) { uploadFail("Неприпустимий розмір прошивки."); return; }
      char hash[65]; memcpy(hash, packageHeader + 20, 64); hash[64] = 0;
      if (!Update.begin(imageSize, U_FLASH) || !Update.setSHA256(hash)) { uploadFail("Не вдалося підготувати розділ оновлення."); return; }
    }
    size_t count = u.currentSize - offset;
    if (count && imageSize) {
      if (imageCount + count > imageSize || Update.write(u.buf + offset, count) != count) { uploadFail("Помилка запису або розміру файла."); return; }
      imageCount += count;
    }
  } else if (u.status == UPLOAD_FILE_END) {
    if (uploadError.isEmpty() && updating) {
      if (headerCount != sizeof(packageHeader) || imageCount != imageSize || !imageSize) uploadFail("Файл передано не повністю.");
      else if (!Update.end()) uploadFail("Прошивка не пройшла перевірку SHA-256 або формату ESP32-S3.");
      else uploadOk = true;
    }
  } else if (u.status == UPLOAD_FILE_ABORTED) { uploadFail("Передавання перервано."); updating = false; }
}
void boardWebBegin() {
  esp_ota_img_states_t state;
  pendingBoot = esp_ota_get_state_partition(esp_ota_get_running_partition(), &state) == ESP_OK && state == ESP_OTA_IMG_PENDING_VERIFY;
  const char *headers[] = { "X-Proptest" };
  web.collectHeaders(headers, 1);
  web.on("/", HTTP_GET, [] { web.sendHeader("Cache-Control", "no-store"); web.send_P(200, "text/html; charset=utf-8", boardPage); });
  web.on("/status", HTTP_GET, [] { if (authorized()) sendStatus(); });
  web.on("/confirm", HTTP_POST, [] {
    if (!authorized()) return;
    auto s = motorSnapshot();
    if (!s.ready || millis() - s.tick > 100 || !wifiReady) { reject("Самоперевірка плати не завершена."); return; }
    if (pendingBoot && esp_ota_mark_app_valid_cancel_rollback() != ESP_OK) { reject("Не вдалося підтвердити прошивку."); return; }
    pendingBoot = false; sendStatus();
  });
  web.on("/arm", HTTP_POST, [] {
    if (!authorized()) return;
    auto s = motorSnapshot();
    if (updating || restartAt || pendingBoot || !s.ready || s.armed || s.applied || millis() < 3000 || millis() - s.tick > 100) {
      reject("Плата ще не готова або тест уже активний."); return;
    }
    uint32_t token; do { token = esp_random(); } while (!token);
    portENTER_CRITICAL(&motorMux);
    motor.armed = true; motor.target = 0; motor.started = motor.heartbeat = millis(); motor.token = token; motor.reason = "STOP";
    portEXIT_CRITICAL(&motorMux);
    sendStatus(true);
  });
  web.on("/gas", HTTP_POST, [] {
    if (!authorized()) return;
    if (!sessionValid()) { reject("Тест завершено. Підготуйте його повторно."); return; }
    String value = web.arg("value");
    bool digits = value.length() > 0 && value.length() <= 3;
    for (unsigned n = 0; n < value.length(); n++) digits &= value[n] >= '0' && value[n] <= '9';
    if (!digits || value.toInt() > 100) { motorStopped(); reject("Дозволено лише від 0 до 10% газу.", 400); return; }
    portENTER_CRITICAL(&motorMux);
    if (motor.armed) { motor.target = value.toInt(); motor.heartbeat = millis(); }
    portEXIT_CRITICAL(&motorMux); sendStatus();
  });
  web.on("/lease", HTTP_POST, [] {
    if (!authorized()) return;
    if (sessionValid()) { portENTER_CRITICAL(&motorMux); if (motor.armed) motor.heartbeat = millis(); portEXIT_CRITICAL(&motorMux); }
    sendStatus();
  });
  web.on("/stop", HTTP_POST, [] { if (authorized()) { if (motorStopped()) sendStatus(); else reject("Зупинку виходу не підтверджено."); } });
  web.on("/update", HTTP_POST, [] {
    if (!authorized()) return;
    if (uploadOk) { web.send(200, "application/json", "{\"ok\":true}"); restartAt = millis() + 800; }
    else { reject(uploadError.length() ? uploadError.c_str() : "Файл відсутній.", 400); updating = false; }
  }, receiveUpload);
  web.begin();
}
void boardWebLoop() {
  web.handleClient();
  if (restartAt && (int32_t)(millis() - restartAt) >= 0) { motorStopped(); ESP.restart(); }
  if (pendingBoot && millis() > 60000) { motorStopped(); esp_ota_mark_app_invalid_rollback_and_reboot(); }
}
