#include "HX711.h" // Бібліотека для тензодатчика
#include <Wire.h> // Must include Wire library for I2C
#include <SparkFun_MMA8452Q.h> // Includes the SFE_MMA8452Q library
MMA8452Q accel;
#define SENSOR_PIN A0
const int sampleWindow = 200; // Sensor speed
const int threshold = 80; // Variable (CAN BE CHANGED)
const float referenceValue = 0.1048; // Replace with the actual reference value

bool started = false;

// Об'єкт і підключення до тензодатчика
HX711 scale;
uint8_t dataPin = 6;
uint8_t clockPin = 7;

// Підключення двигунів
byte ena = 3; // швидкість
byte in1 = 4; // напрям
byte in2 = 5;
byte direction = 1;
// Підключення амперметра до аналогового піну A3


void setup() {
  // налаштувати двигуни
  // датчики
  pinMode(A1, INPUT);
  pinMode(A2, INPUT);
  pinMode(A3, INPUT);

  accel.init();

  pinMode(SENSOR_PIN, INPUT);
  pinMode(ena, OUTPUT); // конфігурація пінів для двигунів
  pinMode(in1, OUTPUT);
  pinMode(in2, OUTPUT);
  scale.begin(dataPin, clockPin); // вмикаємо тензодатчик
  scale.set_scale(686.011047); // TODO you need to calibrate this yourself.
  // reset the scale to zero = 0
  scale.tare(20);
  Serial.begin(115200);
}

void loop() {
  if (Serial.available() > 0) {
    char incoming = Serial.read();
    if (incoming == 's') {
      started = true;
    }
    if(incoming == 'b'){
      if(direction == 1){
        direction = 0;
        //Serial.println("left");
      }else{
        direction = 1;
        // Serial.println("right");
      }
    }
  }

  if (started) {
    scale.tare(20);
    if(direction == 1){
        digitalWrite(in2, 1);
        digitalWrite(in1, 0);
        
      }else{
        digitalWrite(in2, 0);
        digitalWrite(in1, 1);
       
      }
    for (double i = 25.5; i <= 255; i += 25.5) {
      analogWrite(ena, i); // Швидкість обертання двигуна
      delay(3000);

      double kgS = 0;
      if (scale.is_ready()) {
        kgS = scale.get_units(1);
      }
      if (kgS<0){
kgS = kgS * -1;

      }
      double vibrationX =0;
        double vibrationY =0;
        double vibrationZ = 0;
    
    if (accel.available())
    {
        accel.read();
         vibrationX =accel.cx;
         vibrationY =accel.cy;
         vibrationZ =accel.cz;
    }
      //double vibration = analogRead(2) / 10; // для прикладу замінити це значення зчитуванням з датчика



      // Зчитування аналогового значення з піну AMP_PIN
      float current = 0;
      for(int i = 0; i < 1000; i++) 
      {
      current = current + (.0264 * analogRead(A3) -13.51) / 1000;
      delay(1);
      }
  
      // Перетворення аналогового значення на напругу (0-5V)
      
      // Перетворення напруги на струм (використовуйте відповідний коефіцієнт для вашого датчика)
      // Для Keyestudio 20A датчика струму, коефіцієнт може бути 0.1V/A
      
      // Затримка перед наступним вимірюванням
      delay(1000);

      int sample;
      unsigned long startMillis = millis(); // Start of sample window
      float peakToPeak = 0; // peak-to-peak level
      unsigned int signalMax = 0; // minimum value
      unsigned int signalMin = 1024; // maximum value

      while (millis() - startMillis < sampleWindow) {
        sample = analogRead(0); // Значення з мікрофона
        if (sample < 1024) { // toss out spurious readings
          if (sample > signalMax) {
            signalMax = sample; // save just the max levels
          } else if (sample < signalMin) {
            signalMin = sample; // save just the min levels
          }
        }
      }

      double dB = 20 * log10(signalMax / (double)referenceValue);
      if (Serial.available() > 0) {
          char incoming = Serial.read();
            if (incoming == 'g') {
              started = false;
              analogWrite(ena, 0);
              //Serial.println("stop");
               break;
            }
      }
      // Формуємо пакет для відправки
      String data = String(kgS) + "|" + String(vibrationX) + "|" + String(vibrationY) + "|" + String(vibrationZ) + "|" + String(current*1.75) + "|" + String(dB);
      Serial.println(data);
      delay(100);
      
    }
    started = false;
    analogWrite(ena, 0);
}
}
