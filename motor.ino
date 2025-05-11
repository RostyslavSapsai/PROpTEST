#include <HX711.h>
#include <Wire.h>
#include <SparkFun_MMA8452Q.h>
#include <Servo.h> // Для керування ESC

MMA8452Q accel;
Servo myESC; // Об'єкт ESC
#define ESC_PIN 9 // PWM-вихід до ESC

#define SENSOR_PIN A0
const int sampleWindow = 200;
const int threshold = 80;
const float referenceValue = 0.1048;

bool started = false;

int direction = 1; // Початковий напрямок (1 - вперед, 0 - назад)

int currentSpeed = 1050; // Поточна швидкість ESC
int targetSpeed = 1300;  // Цільова швидкість ESC
const int speedStep = 5; // Крок зміни швидкості

unsigned long lastSpeedUpdate = 0;
const int speedUpdateInterval = 50; // Інтервал оновлення швидкості (мс)

HX711 scale;
uint8_t dataPin = 6;
uint8_t clockPin = 7;

void setup() {
  accel.init();
  pinMode(SENSOR_PIN, INPUT);
  
  scale.begin(dataPin, clockPin);
  scale.set_scale(686.011047);
  scale.tare(20);
  Serial.begin(115200);

  // Ініціалізація ESC
  myESC.attach(ESC_PIN);
   myESC.writeMicroseconds(1060); // Ініціалізація ESC
   delay(100);
  myESC.writeMicroseconds(1000); // Мінімальна потужність
  delay(100);
}


void loop() {
  if (Serial.available() > 0) {
    char incoming = Serial.read();
    if (incoming == 's') {
      started = true;
    }
    if (incoming == 'b') {
      direction = 1 - direction; // Перемикач напрямку
    }
    if (incoming == 'g') {
      started = false;
      myESC.writeMicroseconds(1000); // Зменшуємо швидкість до мінімуму при зупинці
    }
  }

  if (started) {
    scale.tare(20);

    for (int speed = 1060; speed < 1160; speed += 1){
      myESC.writeMicroseconds(speed);
    double kgS = 0;
    if (scale.is_ready()) {
      kgS = scale.get_units(1);
    }
    if (kgS < 0)
    { 
      kgS *= -1;
    }

    double vibrationX = 0, vibrationY = 0, vibrationZ = 0;
    if (accel.available()) {
      accel.read();
      vibrationX = accel.cx;
      vibrationY = accel.cy;
      vibrationZ = accel.cz;
    }
    delay(500);

    int sample;
    unsigned long startMillis = millis();
    float peakToPeak = 0;
    unsigned int signalMax = 0;
    unsigned int signalMin = 1024;

    while (millis() - startMillis < sampleWindow) {
      sample = analogRead(0);
      if (sample < 1024) {
        if (sample > signalMax) {
          signalMax = sample;
        } else if (sample < signalMin) {
          signalMin = sample;
        }
      }
    }

    double dB = 20 * log10(signalMax / (double)referenceValue);
    if (Serial.available() > 0) {
          char incoming = Serial.read();
            if (incoming == 'g') {
              started = false;
              //Serial.println("stop");
               break;
            }
      }
      float current = 0;
      for(int i = 0; i < 1000; i++) 
      {
      current = current + (.0264 * analogRead(A3) -13.51) / 1000;
      delay(1);
      }
    String data = String(kgS) + "|" + String(vibrationX) + "|" + String(vibrationY) + "|" + String(vibrationZ) + "|" + String(current*1.75 + 0.02) + "|" + String(dB);
    Serial.println(data);
    delay(100);
  }
  started = false;
  myESC.writeMicroseconds(1000); 
  }  
}
