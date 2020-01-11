using System;
using System.IO.Ports;

namespace ObsJockey
{
    class Arduinobs
    {
        public static bool ParseArduinoData(SerialPort p, String command, float[] data)
        {
            bool retval = false;

            p.Write(command + "\n");
            String response = p.ReadLine().Trim();

            String[] elements = response.Split(' ');

            if (elements.Length == data.Length)
            {
                try
                {
                    for (int i = 0; i < elements.Length; i++)
                    {
                        data[i] = Single.Parse(elements[i]);
                    }
                    retval = true;
                }
                catch (Exception)
                {
                    /* We won't cause any fatal errors, and instead return a soft error via the return. */
                }
            }

            return retval;
        }

        public static bool QueryTiltData(SerialPort p, out float x, out float y, out float z)
        {
            float[] data = new float[3];
            x = -9999;
            y = -9999;
            z = -9999;

            bool retval = ParseArduinoData(p, "tilt", data);

            if (retval)
            {
                x = data[0];
                y = data[1];
                z = data[2];
            }

            return retval;
        }

        public static bool QueryAmbientData(SerialPort p, out float temp, out float pressure, out float humidity)
        {
            float[] data = new float[3];
            temp = -9999;
            pressure = -9999;
            humidity = -9999;

            bool retval = ParseArduinoData(p, "ambient", data);

            if (retval)
            {
                temp = data[0];
                pressure = data[1];
                humidity = data[2];
            }

            return retval;
        }

        public static bool QueryFanRate(SerialPort p, out float fan_rate)
        {
            float[] data = new float[1];
            fan_rate = -9999;

            bool retval = ParseArduinoData(p, "get_tach", data);

            if (retval)
            {
                fan_rate = data[0];
            }

            return retval;
        }

        public static bool InitTiltSensor(SerialPort p)
        {
            p.Write("init_bno055\n");
            return p.ReadLine().Trim().CompareTo("OK") == 0;
        }

        public static bool InitAmbientSensor(SerialPort p)
        {
            p.Write("init_bme280\n");
            return p.ReadLine().Trim().CompareTo("OK") == 0;
        }

        public static float pascalsToInches(float pressure)
        {
            return pressure * 0.0002953f;
        }

        public static float calcSeaPressure(float temperature, float pressure, int altitude)
        {
            float adj_temp = temperature + 273.15f;
            float tempGradient = 0.0065f;

            float v3 = adj_temp + tempGradient * altitude;
            float sealevelPressure = (float)(pressure / Math.Pow((1 - tempGradient * altitude / v3), .03416f / tempGradient));
            sealevelPressure = (float)Math.Round(sealevelPressure * 100) / 100;
            return sealevelPressure;
        }
    }
}
