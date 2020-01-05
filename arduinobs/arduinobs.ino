/**
 * Arduino interface/bridge between the Win10-based obs-jockey and Pier #1 sensors.
 * 
 * Currently supported:
 * - BNO055 x/y/z query
 * - BME280 temperature/pressure/humidity query
 * - Fan tach speed (percentage)
 */

#include <TimerThree.h>
#include <Wire.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_BME280.h>
#include <Adafruit_BNO055.h>
#include <utility/imumaths.h>

//#define DEBUG
#define array_dim_sizeof(x)   (sizeof(x) / sizeof(x[0]))
#define RESP_ERR  "ERROR"
#define RESP_OK   "OK"

typedef struct {
  float x;
  float y;
  float z;
} tilt_t;

typedef struct {
  float temp;
  float pres;
  float hum;
} amb_t;

/* Ambient sensor object. */
Adafruit_BME280 bme;

/* Tilt sensor object. */
Adafruit_BNO055 bno = Adafruit_BNO055(55);

/* Current 1-second-average tach reading. */
volatile unsigned int tach = 0;

/* Fan tach counter; internal interrupt use only. */
volatile unsigned int tach_cnt = 0;

/**
 * Change interrupt for the tach line.
 * 
 * We simply increment the tach count on each edge, and the 1Hz timer captures the count to give us a high-ish precision percentage.
 */
void tach_line_change(void)
{
  /* Got a tach edge, so increment the tach count. */
  tach_cnt++;
}

/**
 * Timer 1 interrupt at 1Hz.
 * 
 * We flip the LED each second, and also capture the current fan tach for monitoring purposes.
 */
ISR(TIMER1_COMPA_vect)
{
  /* Static variable to track the current LED state. */
  static bool led_state = false;

  /* Capture the current tachometer for any status requests. */
  tach = tach_cnt;
  tach_cnt = 0;

  /* Take care of the LED flashing. */
  if (led_state) {
    digitalWrite(13, HIGH);
  } else {
    digitalWrite(13, LOW);
  }
  led_state = !led_state;  
}

/**
 * Standard Arduino setup function.
 */
void setup(void)
{
  /* Set a reasonable baud for serial comms. */
  Serial.begin(115200);

  /* Set a "0" serial port timeout so that we get immediate feedback on commands. */
  Serial.setTimeout(0);
  
  /* LED */
  pinMode(13, OUTPUT);

  /* Fan PWM */
  Timer3.initialize(40);

  /* Tach counter */
  pinMode(7, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(7), tach_line_change, CHANGE);

  /* BNO55 Reset */
  pinMode(4, OUTPUT);
  digitalWrite(4, LOW);
  delay(10);
  digitalWrite(4, HIGH);

  /* Start the BNO055 (tilt) sensor */
  if(!bno.begin())
  {
    /* There was a problem detecting the BNO055.  Die in a loop since all is useless without it. */
    Serial.println("BNO055 detection failed!");
    while(1);
  }
  
  if (!bme.begin())
  {
    Serial.println("BME280 detection failed!");
    while(1);
  }

  /* The Adafruit BNO055 board has an external crystal.  Make sure we use it. */
  bno.setExtCrystalUse(true);
    
  /* Set up 1Hz timer for LED flashing and monitoring the fan tach. */
  TCCR1A = 0; /* Set entire TCCR1A register to 0. */
  TCCR1B = 0; /* Same for TCCR1B. */
  TCNT1  = 0; /* Initialize counter value to 0 */
  /* Set compare match register for 1hz increments. */
  OCR1A = 15624;// = (16*10^6) / (1*1024) - 1 (NB: must be <65536)
  /* turn on CTC mode. */
  TCCR1B |= (1 << WGM12);
  /* Set CS12 and CS10 bits for 1024 prescaler. */
  TCCR1B |= (1 << CS12) | (1 << CS10);  
  /* enable timer compare interrupt. */
  TIMSK1 |= (1 << OCIE1A);

  /* Start with the fan off. */
  Timer3.pwm(5, 0);
}

/**
 * Acquire tilt data from the BNO055.
 * 
 * @param t   A tilt_t struct that will be populated with X/Y/Z data from the sensor.
 */
void get_tilt(tilt_t *t)
{
  sensors_event_t event; 
  
  if (!t || !bno.getEvent(&event)) {
    /* This is a bad news situation with a bad pointer and bad sensor comms, so we'll fill out bogus data. */
    t->x = -9999;
    t->y = -9999;
    t->z = -9999;
  } else {
    /* Successful sensor data acquisition. */
    t->x = event.orientation.x; 
    t->y = event.orientation.y; 
    t->z = event.orientation.z; 
  }
}

/**
 * Acquire ambient data from the BME280.
 * 
 * @param a   An amb_t struct that will be populated with temperature, pressure, and humidity data.
 */
void get_ambient(amb_t *a)
{
  if (!a) {
    /* Bad pointer, which is bad news.  Fill out some bogus data. */
    a->temp = -9999;
    a->pres = -9999;
    a->hum  = -9999;
  } else {
    a->temp = bme.readTemperature();
    a->pres = bme.readPressure();
    a->hum  = bme.readHumidity();
  }
}

/* Debug stuff.  Not necessary for production build. */
#ifdef DEBUG
void print_sensors(void)
{
  tilt_t t;
  amb_t a;

  get_tilt(&t);
  get_ambient(&a);

  Serial.println("**************");
  Serial.print("X: ");
  Serial.print(t.x);
  Serial.print("\tY: ");
  Serial.print(t.y);
  Serial.print("\tZ: ");
  Serial.print(t.z);
  Serial.println("");
  
  Serial.print("Temp: ");
  Serial.print(a.temp);
  Serial.println(" C");
  Serial.print("Pressure: ");
  Serial.print(a.pres);
  Serial.println("");
  Serial.print("Humidity: ");
  Serial.print(a.hum);
  Serial.println("");

  Serial.print("Tach %: ");
  Serial.print(tach);
  Serial.println("");
  Serial.println("**************");
  Serial.println("");
}
#endif

String get_serial_command(String cmd)
{
  return Serial.readStringUntil('\n');
}

void handle_tilt(String params)
{
  tilt_t t;
  get_tilt(&t);
  Serial.println(String(t.x) + " " + String(t.y) + " " + String(t.z));
}

void handle_ambient(String params)
{
  amb_t a;
  get_ambient(&a);
  Serial.println(String(a.temp) + " " + String(a.pres) + " " + String(a.hum));
}

void handle_get_tach(String params)
{
  Serial.println(String(tach));
}

void handle_set_tach(String params)
{
  enum { MAX_STR_LEN = 16 };
  char buf[MAX_STR_LEN];

  /* Start out assuming a number as long as there's a parameter present. */
  bool is_number = (params.length() > 0);
  
  /* Convert the string to a character array for processing. */
  params.toCharArray(buf, MAX_STR_LEN);

  /* Make sure it's a number. */
  for (int i = 0; i < MAX_STR_LEN && buf[i] && is_number; i++) {
    if (buf[i] < '0' || buf[i] > '9') {
      is_number = false;
    }
  }

  if (!is_number) {
    Serial.println(RESP_ERR);
  } else {
    /* Make sure the tach is between 0 and 100. */
    int tach_req = atoi(buf);

    if (tach_req < 0 || tach_req > 100) {
      Serial.println(RESP_ERR);
    } else {
      int actual = (tach_req * 1023) / 100;
      Timer3.pwm(5, actual);
      Serial.println(RESP_OK);
    }
  }
}

void handle_init_bno055(String params)
{
  /* The BNO interface only supports a "begin" and no specific "init", so we call it. */
  if (bno.begin()) {
    Serial.println(RESP_OK);
  } else {
    Serial.println(RESP_ERR);
  }
}

void handle_init_bme280(String params)
{
  /* The BME supports a specific init, so we'll use it. */
  if (bme.init()) {
    Serial.println(RESP_OK);
  } else {
    Serial.println(RESP_ERR);
  }
}

struct {
  String cmd;
  void (*cmd_handler)(String);
} commands[] = {
  { "tilt", &handle_tilt },
  { "ambient", &handle_ambient },
  { "get_tach", &handle_get_tach },
  { "set_tach", &handle_set_tach },
  { "init_bno055", &handle_init_bno055 },
  { "init_bme280", &handle_init_bme280 }
};

void loop()
{
#ifdef DEBUG
  Timer3.pwm(5, 256);
  delay(1000);
  
  while(1) {
    print_sensors();
    delay(1000);
  }
#else
  while(1) {
    int i;
    String req = "";
    String cmd = "";
    String params = "";
    
    /* Read a string from the buffer and remove any whitespace. */
    do {
      int data = Serial.read();
      if (data != -1) {
        req += String((char)data);
      }
    } while (!req.endsWith("\n") && !req.endsWith("\r"));
    req.trim();

    /* Split out the command from the parameters. */
    int param_index = req.indexOf(' ');

    if (param_index > 0) {
      cmd = req.substring(0, param_index);
      params = req.substring(param_index + 1);
    } else {
      cmd = req;
    }
    
    /* Search for a command match. */
    for (i = 0; i < array_dim_sizeof(commands); i++) {
      if (cmd.compareTo(commands[i].cmd) == 0) {
        break;
      }
    }

    /* Return an error if no command was found; otherwise run the handler. */
    if (i >= array_dim_sizeof(commands)) {
      Serial.println(RESP_ERR);
    } else {
      (*commands[i].cmd_handler)(params);
    }
  }
#endif
}
