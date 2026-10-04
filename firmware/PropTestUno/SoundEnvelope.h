#pragma once
#include <stdint.h>

// Mean peak-to-peak amplitude of complete 50 ms windows, not calibrated sound pressure.
struct SoundEnvelope {
  uint32_t started = 0, total = 0;
  uint16_t low = 1023, high = 0, count = 0, windows = 0;
  uint16_t lastLow = 0, lastHigh = 0, lastCount = 0;
  void reset(uint32_t now) {
    started = now; total = windows = count = 0; low = 1023; high = 0;
    lastLow = lastHigh = lastCount = 0;
  }
  void add(uint16_t value, uint32_t now) {
    if (now - started >= 50) {
      lastLow = low; lastHigh = high; lastCount = count;
      // Long stalls must not be passed off as a normally sampled window.
      if (now - started < 100 && count >= 10) {
        if (windows == 1000) { total = 0; windows = 0; }
        total += high - low; windows++;
      }
      started = now; count = 0; low = 1023; high = 0;
    }
    if (value < low) low = value;
    if (value > high) high = value;
    count++;
  }
  float mean() const { return windows ? (float)total / windows : 0; }
  void consume() { total = 0; windows = 0; }
};
