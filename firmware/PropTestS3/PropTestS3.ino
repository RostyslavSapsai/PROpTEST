#include <WiFi.h>
#include <Preferences.h>
#include <esp_system.h>
#include <ESPmDNS.h>

static const char *identity = "PT:S3:0.4";
WiFiServer server(8768);
WiFiClient client;
String ssid, password;
String homeSsid, homePassword, hostname;
uint32_t joinStarted = 0;
bool wifiReady = false;
#include "BoardWeb.h"
struct Receiver { String line; bool overflow = false; } usbRx, uartRx, wifiRx;

bool decodeHex(const String &hex, String &result) {
  result = "";
  if (hex.length() % 2) return false;
  for (unsigned n = 0; n < hex.length(); n += 2) {
    auto digit = [](char c) -> int { if (c >= '0' && c <= '9') return c - '0'; if (c >= 'A' && c <= 'F') return c - 'A' + 10; if (c >= 'a' && c <= 'f') return c - 'a' + 10; return -1; };
    int a = digit(hex[n]), b = digit(hex[n + 1]);
    if (a < 0 || b < 0 || (a == 0 && b == 0)) return false;
    result += (char)(a * 16 + b);
  }
  return true;
}

void networkStatus(Stream &stream) {
  stream.print("NETWORK|");
  stream.print(WiFi.status() == WL_CONNECTED ? "CONNECTED" : homeSsid.length() == 0 ? "NOT_CONFIGURED" : (uint32_t)(millis() - joinStarted) < 20000 ? "CONNECTING" : "UNAVAILABLE");
  stream.print('|'); stream.print(WiFi.localIP());
  stream.print('|'); stream.print(hostname); stream.println(".local");
}

void command(Stream &stream, const String &line, bool local) {
  if (line == "P") stream.println(identity);
  else if (line == "H") { stream.println("PONG"); networkStatus(stream); }
  else if (line == "I") {
    stream.print("INFO|PROpTEST v2|S3-Connect " PROPTEST_VERSION "|NO_SENSORS|WEB_MOTORS_GPIO10_13_CAP10|");
    stream.print(wifiReady ? ssid : "WIFI_FAILED");
    stream.print('|'); stream.print(WiFi.softAPIP()); stream.println("|8768");
    // Credentials are available over the physical USB/UART link only.
    if (local && wifiReady) { stream.print("WIFIKEY|"); stream.println(password); }
    networkStatus(stream);
  } else if (line.startsWith("NET:")) {
    if (!local) { stream.println("ERR:NET:USB_ONLY"); return; }
    int split = line.indexOf(':', 4);
    String name, key;
    if (split < 0 || !decodeHex(line.substring(4, split), name) || !decodeHex(line.substring(split + 1), key)
        || name.length() < 1 || name.length() > 32 || key.length() < 8 || key.length() > 63) {
      stream.println("ERR:NET:INVALID"); return;
    }
    Preferences prefs;
    if (!prefs.begin("proptest", false)) { stream.println("ERR:NET:STORE"); return; }
    bool stored = prefs.putString("homeSsid", name) > 0 && prefs.putString("homeKey", key) > 0;
    prefs.end();
    if (!stored) { stream.println("ERR:NET:STORE"); return; }
    homeSsid = name; homePassword = key;
    WiFi.disconnect();
    WiFi.begin(homeSsid.c_str(), homePassword.c_str());
    joinStarted = millis();
    stream.println("ACK:NET");
  } else if (line == "G") stream.println(motorStopped() ? "ACK:G:0" : "ERR:STOP");
  else stream.println("ERR:CONNECTION_ONLY");
}

void receive(Stream &stream, Receiver &rx, bool local) {
  // Bound work per link so an incomplete or flooding client cannot starve USB.
  for (int n = 0; n < 128 && stream.available(); n++) {
    char c = stream.read();
    if (c == '\r') continue;
    if (c == '\n') {
      if (rx.overflow) stream.println("ERR:LINE_TOO_LONG");
      else if (rx.line.length()) command(stream, rx.line, local);
      rx.line = ""; rx.overflow = false;
    } else if (rx.line.length() < 200 && !rx.overflow) rx.line += c;
    else { rx.line = ""; rx.overflow = true; }
  }
}

void setup() {
  motorBegin();
  Serial.begin(115200);   // Native USB CDC (USB connector).
  Serial0.begin(115200);  // USB-to-UART connector, when fitted.
  Preferences preferences;
  preferences.begin("proptest", false);
  password = preferences.getString("apKey", "");
  homeSsid = preferences.getString("homeSsid", "");
  homePassword = preferences.getString("homeKey", "");
  if (password.length() != 16) {
    char key[17];
    snprintf(key, sizeof(key), "%08lX%08lX", (unsigned long)esp_random(), (unsigned long)esp_random());
    password = key;
    preferences.putString("apKey", password);
  }
  preferences.end();
  char name[32];
  snprintf(name, sizeof(name), "PROpTEST-v2-%06lX", (unsigned long)(ESP.getEfuseMac() & 0xffffff));
  ssid = name;
  hostname = ssid; hostname.toLowerCase();
  WiFi.setHostname(hostname.c_str());
  WiFi.mode(WIFI_AP_STA);
  WiFi.setAutoReconnect(true);
  wifiReady = WiFi.softAP(ssid.c_str(), password.c_str());
  if (wifiReady) server.begin();
  MDNS.begin(hostname.c_str());
  MDNS.addService("proptest", "tcp", 8768);
  if (homeSsid.length()) { WiFi.begin(homeSsid.c_str(), homePassword.c_str()); joinStarted = millis(); }
  boardWebBegin();
}

void loop() {
  receive(Serial, usbRx, true);
  receive(Serial0, uartRx, true);
  if (wifiReady && (!client || !client.connected())) {
    client.stop(); client = server.accept(); wifiRx = Receiver();
    if (client) { client.setNoDelay(true); client.setTimeout(100); }
  }
  if (client && client.connected()) receive(client, wifiRx, false);
  boardWebLoop();
  delay(1);
}
