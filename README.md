> **Актуальна документація — 15 вересня 2026 року:** [PROpTEST: стан, інструкція користувача, обладнання, архітектура, протокол і план v2](docs/current/README.md).
>
> Файли коду в корені цього репозиторію є історичними Arduino-скетчами. Описана в документації локальна Windows-програма Engineering v0.14 та прошивка Uno PT:2.2 цією публікацією не додаються. Wi-Fi, ESP32-S3, Matek і чотири моторні канали — наступний етап, не готові функції поточної версії. Звук зараз вимірюється як ADC, не калібровані dB SPL.

---
Propeller Testing Device

A modular device designed to measure key physical characteristics of propellers, such as thrust, sound, current, and vibration. This project aims to simplify the measuring of propeller technical characteristics and support STEM education and allow students to explore aerodynamics and propulsion in a hands-on, interactive way.

🔧 Features

Real-time measurement of thrust, sound, current, and vibration
Modular 3D-printed frame
Custom ESC + brushless motor integration
Easy sensor configuration and data logging
Educational methodology materials (in progress)

🎓 Educational Use

This device is intended for use in physics and engineering classrooms to explore forces, efficiency, and electronics. Methodological recommendations are being developed for alignment with school curricula.
Planned expansions include:
Mobile application for live monitoring
Web platform for result tracking and comparison
Multi-language support

🧪 Technologies Used

Sound sensor, ​
Current sensor, ​
Controller, ​
Accelerometer, ​
Drone motor (low power, mechanical), ​
Battery/power supply,​
motor driver for the first type of kit (for educational purposes) and for the second (for testing propellers for further use):​
ESC is used instead of the motor driver,​
Brushless motor is used​

🌐 License

This project is licensed under the Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International License.Read more: http://creativecommons.org/licenses/by-nc-sa/4.0/
You are free to use, share, and adapt this work — for non-commercial purposes only — and must attribute the original authors.
📬 Contact
For collaboration, feedback or educational partnerships, contact:[rostuslav.sapsai@gmail.com] Visit our website : https://sites.google.com/view/proptest/home?authuser=0


​Libraries:
acs712 library: https://github.com/RobTillaart/ACS712
HX711-master library: https://github.com/bogde/HX711
SparkFun_MMA8452Q_Arduino_Library_master library: https://github.com/sparkfun/SparkFun_MMA8452Q_Arduino_Library
Wire library: https://docs.arduino.cc/language-reference/en/functions/communication/wire/
