#include <SoftwareSerial.h>

// HC-05/HC-06 UART connection:
//   module TX -> Arduino D10 (SoftwareSerial RX)
//   module RX -> Arduino D9  (SoftwareSerial TX, use a  voltage divider)
SoftwareSerial bt(10, 9);

const long USB_BAUD = 115200;
// Match the HC-05/HC-06 UART speed configured for this module.
// This project uses 38400 (the module's configured data-mode speed).
const long BLUETOOTH_BAUD = 38400;

const byte JOY1_X = A0;
const byte JOY1_Y = A1;
const byte JOY2_X = A2;
const byte JOY2_Y = A3;
const byte RESET_BUTTON = 2;
const byte CAMERA_BUTTON = 3;

void setup() {
  pinMode(RESET_BUTTON, INPUT_PULLUP);
  pinMode(CAMERA_BUTTON, INPUT_PULLUP);

  Serial.begin(USB_BAUD);
  bt.begin(BLUETOOTH_BAUD);
}

void loop() {
  // Active-low buttons. The Unity receiver expects seven integer fields:
  // joy1_x, joy1_y, joy2_x, joy2_y, brake, camera, reset
  // The right-stick Y value is used for both throttle and brake by Unity.
  int brake = analogRead(JOY2_Y) > 560 ? 1 : 0;
  int camera = digitalRead(CAMERA_BUTTON) == LOW ? 1 : 0;
  int reset = digitalRead(RESET_BUTTON) == LOW ? 1 : 0;

  int joy1_x = analogRead(JOY1_X);
  int joy1_y = analogRead(JOY1_Y);
  int joy2_x = analogRead(JOY2_X);
  int joy2_y = analogRead(JOY2_Y);

  // Send to both outputs: USB is useful for the Serial Monitor, while the
  // paired Bluetooth module is the connection Unity uses during flight.
  sendPacket(Serial, joy1_x, joy1_y, joy2_x, joy2_y, brake, camera, reset);
  sendPacket(bt, joy1_x, joy1_y, joy2_x, joy2_y, brake, camera, reset);

  delay(20); // approximately 50 joystick updates per second
}

void sendPacket(Print& output, int joy1_x, int joy1_y, int joy2_x, int joy2_y,
                int brake, int camera, int reset) {
  output.print(joy1_x);
  output.print(',');
  output.print(joy1_y);
  output.print(',');
  output.print(joy2_x);
  output.print(',');
  output.print(joy2_y);
  output.print(',');
  output.print(brake);
  output.print(',');
  output.print(camera);
  output.print(',');
  output.println(reset);
}
