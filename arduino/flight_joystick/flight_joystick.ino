// Skybound flight joystick
// Arduino Uno + two-axis joystick module + optional throttle potentiometer.
// Upload this sketch from the Arduino IDE.

// Joystick 1: flight attitude
const int JOY1_X = A0;      // roll, like keyboard A/D
const int JOY1_Y = A1;      // pitch, like keyboard UP/DOWN
// Joystick 2: engine and heading
const int JOY2_X = A2;      // yaw, like keyboard Q/E
const int JOY2_Y = A3;      // throttle, like keyboard W/S
const int RESET_BUTTON = 2;  // left joystick SW: reset/restart
const int CAMERA_BUTTON = 3; // right joystick SW: camera

void setup() {
  Serial.begin(115200);
  pinMode(RESET_BUTTON, INPUT_PULLUP);
  pinMode(CAMERA_BUTTON, INPUT_PULLUP);
  pinMode(RESET_BUTTON, INPUT_PULLUP);
}

void loop() {
  int joy1_x = analogRead(JOY1_X);
  int joy1_y = analogRead(JOY1_Y);
  int joy2_x = analogRead(JOY2_X);
  int joy2_y = analogRead(JOY2_Y);
  int brake = 0; // braking remains available from the keyboard SPACE key
  int camera = digitalRead(CAMERA_BUTTON) == LOW ? 1 : 0;
  int reset = digitalRead(RESET_BUTTON) == LOW ? 1 : 0;

  // CSV format read by Unity (ArduinoJoystick.cs):
  // joy1_x,joy1_y,joy2_x,joy2_y,brake,camera,reset
  Serial.print(joy1_x);
  Serial.print(',');
  Serial.print(joy1_y);
  Serial.print(',');
  Serial.print(joy2_x);
  Serial.print(',');
  Serial.print(joy2_y);
  Serial.print(',');
  Serial.print(brake);
  Serial.print(',');
  Serial.print(camera);
  Serial.print(',');
  Serial.println(reset);
  delay(20); // 50 joystick updates per second
}
