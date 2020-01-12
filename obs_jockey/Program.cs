using System;
using System.IO.Ports;
using System.Threading;

namespace ObsJockey
{
    class Program
    {
        static String FilterPosToString(int pos)
        {
            String retval = "ERROR";

            if (pos == -1)
            {
                retval = "[MOVING]";
            }
            else if (pos >= 1 && pos <= 8)
            {
                retval = fw_mapping[pos - 1];
            }

            return retval;
        }

        static void Main()
        {
            CyberPowerData ups = new CyberPowerData();
            SerialPort p;

            /* Attempt to open the serial port.  Inability to do so is fatal. */
            try
            {
                p = new SerialPort(_port, 115200, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    RtsEnable = true,
                    ReadTimeout = SerialPort.InfiniteTimeout
                };
                p.Open();
            }
            catch (Exception e)
            {
                Console.WriteLine("FATAL: Could not open serial port {0}!");
                Console.WriteLine("\t" + e.Message);
                return;
            }
            
            /* Attempt to initialize the sensors.  Inability to do so is fatal. */
            if (!Arduinobs.InitTiltSensor(p))
            {
                Console.WriteLine("FATAL: Could not initialize the tilt sensor!");
                p.Close();
                return;
            }

            if (!Arduinobs.InitAmbientSensor(p))
            {
                Console.WriteLine("FATAL: Could not initialize the ambient sensor!");
                p.Close();
                return;
            }

            Console.WriteLine("RigRunner Test 1...");
            RigRunner.SetPower(2, true);
            Thread.Sleep(1000);
            Console.WriteLine("RigRunner Test 2...");
            RigRunner.SetPower(2, false);
            Thread.Sleep(1000);

            for (int i = 0; i < 100; i++)
            {
                /* Start a query of the UPS data. */
                ups.RefreshData();
                /* FIXME: This isn't correct (obviously); should move UPS stuff to a class and save state. */
                bool validUPS = true;

                /* Query all sensor data. */
                bool validTilt = Arduinobs.QueryTiltData(p, out float x, out float y, out float z);
                bool validAmbient = Arduinobs.QueryAmbientData(p, out float temp, out float pressure, out float humidity);
                bool validFan = Arduinobs.QueryFanRate(p, out float tach_rate);

                /* Ping SGP for data. */
                SequenceGeneratorPro.QuerySGPDevice("Telescope", out SequenceGeneratorPro.SgpDeviceResponse sg_telescope_resp);
                SequenceGeneratorPro.QuerySGPDevice("Camera", out SequenceGeneratorPro.SgpDeviceResponse sg_camera_resp);
                SequenceGeneratorPro.QuerySGPDevice("FilterWheel", out SequenceGeneratorPro.SgpDeviceResponse sg_fw_resp);
                SequenceGeneratorPro.QuerySGPDevice("Focuser", out SequenceGeneratorPro.SgpDeviceResponse sg_focuser_resp);
                SequenceGeneratorPro.QuerySGPScopePosition(out SequenceGeneratorPro.SgpTelescopePosResponse sg_position_resp);
                SequenceGeneratorPro.QuerySGPFilterPosition(out SequenceGeneratorPro.SgpFilterPosResponse sg_filterpos_resp);
                SequenceGeneratorPro.QuerySGPFocuserPosition(out SequenceGeneratorPro.SgpFocuserPosResponse sg_focuspos_resp);
                SequenceGeneratorPro.QuerySGPCameraTemp(out SequenceGeneratorPro.SgpCameraTempResponse sg_cameratemp_resp);

                RigRunner.QueryRigRunner(out RigRunner.RigRunnerStatus rr_status);

                /* Wait for the UPS query to finish before proceeding. */
                ups.upsDataEvent.WaitOne();

                /* Now print the report. 
                 * FIXME: This is temporary during initial development.
                 */
                Console.WriteLine("**************************");
                Console.WriteLine(" Pass " + i);
                Console.WriteLine("**************************");

                Console.WriteLine("-- UPS Data --");
                if (validUPS)
                {
                    Console.Write(ups.ToString());
                }
                else
                {
                    Console.WriteLine("WARNING: Unable to query UPS data!");
                }

                Console.WriteLine("");
                Console.WriteLine("-- Ambient Data --");
                if (validAmbient)
                {
                    Console.WriteLine("Temperature: {0:0.00}°C", temp);
                    Console.WriteLine("Pressure (at altitude): {0:0.00} inHg", Arduinobs.pascalsToInches(pressure));
                    Console.WriteLine("Pressure (sea level): {0:0.00} inHg", Arduinobs.pascalsToInches(Arduinobs.calcSeaPressure(temp, pressure, _altitude)));
                    Console.WriteLine("Humidity: {0:0.00}%", humidity);
                }
                else
                {
                    Console.WriteLine("WARNING: Unable to query ambient data!");
                }

                Console.WriteLine("");
                Console.WriteLine("-- Tilt Data --");
                if (validTilt)
                {
                    Console.WriteLine("X: {0:0.00}°", x);
                    Console.WriteLine("Y: {0:0.00}°", y);
                    Console.WriteLine("Z: {0:0.00}°", z);
                }
                else
                {
                    Console.WriteLine("WARNING: Unable to query tilt data!");
                }

                Console.WriteLine("");
                Console.WriteLine("-- Fan Data --");
                if (validFan)
                {
                    Console.WriteLine("Fan rate: {0:0}%", tach_rate);
                }
                else
                {
                    Console.WriteLine("WARNING: Unable to query fan data!");
                }

                Console.WriteLine("");
                Console.WriteLine("-- Equipment Status --");
                if ((sg_telescope_resp != null) && sg_telescope_resp.Success)
                {
                    Console.WriteLine("Telescope: " + sg_telescope_resp.Message);
                    if (sg_telescope_resp.State != "DISCONNECTED")
                    {
                        if ((sg_position_resp != null) && sg_position_resp.Success)
                        {
                            Console.WriteLine(" └ Position: {0:0.00} RA / {1:0.00} DEC", sg_position_resp.Ra, sg_position_resp.Dec);
                        }
                        else
                        {
                            Console.WriteLine("\tWARNING: Unable to query telescope position!");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("WARNING: Telescope query failed!");
                }

                if ((sg_camera_resp != null) && sg_camera_resp.Success)
                {
                    Console.WriteLine("Camera: " + sg_camera_resp.Message);
                    if (sg_camera_resp.State != "DISCONNECTED")
                    {
                        if ((sg_cameratemp_resp != null) && sg_cameratemp_resp.Success)
                        {
                            Console.WriteLine(" └ Temperature: {0:0.0}°C", sg_cameratemp_resp.Temperature);
                        }
                        else
                        {
                            Console.WriteLine("\tWARNING: Unable to query filter wheel position!");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("WARNING: Camera query failed!");
                }

                if ((sg_fw_resp != null) && sg_fw_resp.Success)
                {
                    Console.WriteLine("Filter Wheel: " + sg_fw_resp.Message);
                    if (sg_fw_resp.State != "DISCONNECTED")
                    {
                        if ((sg_filterpos_resp != null) && sg_filterpos_resp.Success)
                        {
                            Console.WriteLine(" └ Position: " + FilterPosToString(sg_filterpos_resp.Position));
                        }
                        else
                        {
                            Console.WriteLine("\tWARNING: Unable to query filter wheel position!");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("WARNING: Filter wheel query failed!");
                }

                if ((sg_focuser_resp != null) && sg_focuser_resp.Success)
                {
                    Console.WriteLine("Focuser: " + sg_focuser_resp.Message);
                    if (sg_focuser_resp.State != "DISCONNECTED")
                    {
                        if ((sg_focuspos_resp != null) && sg_focuser_resp.Success)
                        {
                            Console.WriteLine(" └ Position: " + sg_focuspos_resp.Position);
                        }
                        else
                        {
                            Console.WriteLine("\tWARNING: Unable to query filter wheel position!");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("WARNING: Focuser query failed!");
                }

                Console.WriteLine("");
                Console.WriteLine("-- Power Status --");
                if (rr_status != null)
                {
                    Console.WriteLine("Main Supply: {0:0.00}V", rr_status.Supply);
                    Console.WriteLine(" └ Total Load: {0:0.00}A", rr_status.Rail0_load + rr_status.Rail1_load + rr_status.Rail2_load + rr_status.Rail3_load + rr_status.Rail4_load);
                    Console.WriteLine("LattePanda: {0}", rr_status.Rail0_enabled ? "ON" : "OFF");
                    if (rr_status.Rail0_enabled)
                    {
                        Console.WriteLine(" └ Load: {0:0.00}A", rr_status.Rail0_load);
                    }
                    Console.WriteLine("STF-8300M: {0}", rr_status.Rail1_enabled ? "ON" : "OFF");
                    if (rr_status.Rail1_enabled)
                    {
                        Console.WriteLine(" └ Load: {0:0.00}A", rr_status.Rail1_load);
                    }
                    Console.WriteLine("Mesu 200 MkII: {0}", rr_status.Rail2_enabled ? "ON" : "OFF");
                    if (rr_status.Rail2_enabled)
                    {
                        Console.WriteLine(" └ Load: {0:0.00}A", rr_status.Rail2_load);
                    }
                    Console.WriteLine("Starlight Focuser Boss II: {0}", rr_status.Rail3_enabled ? "ON" : "OFF");
                    if (rr_status.Rail3_enabled)
                    {
                        Console.WriteLine(" └ Load: {0:0.00}A", rr_status.Rail3_load);
                    }
                    Console.WriteLine("Aux Rail: {0}", rr_status.Rail4_enabled ? "ON" : "OFF");
                    if (rr_status.Rail4_enabled)
                    {
                        Console.WriteLine(" └ Load: {0:0.00}A", rr_status.Rail4_load);
                    }
                }
                else
                {
                    Console.WriteLine("WARNING: RigRunner is offline!");
                }
                

                Console.WriteLine("");
                Console.WriteLine("**************************");

                Console.WriteLine("");

                Thread.Sleep(250);
            }

            p.Close();
        }

        private const String _port = "COM7";
        private const int    _altitude = 154;

        private static readonly String[] fw_mapping =
        {
                "Red",
                "Green",
                "Blue",
                "Luminance",
                "Ha",
                "OIII",
                "SII",
                "[Unpopulated]"
        };
    }
}
