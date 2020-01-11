using System;
using System.IO.Ports;
using System.Threading;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Xml;

using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;
using Windows.Storage.Streams;

using RestSharp;
using Newtonsoft.Json;

namespace obs_jockey
{
    public abstract class UPSPowerEntry : IComparable<UPSPowerEntry>
    {
        public UPSPowerEntry(ushort reportID, ushort offset, ushort size, String description)
        {
            this.reportID = reportID;
            this.offset = offset;
            this.size = size;
            this.description = description;
        }

        public int CompareTo(UPSPowerEntry p)
        {
            if (this.reportID == p.reportID)
            {
                return this.offset.CompareTo(p.offset);
            }
            else
            {
                return this.reportID.CompareTo(p.reportID);
            }
        }

        public abstract override String ToString();

        public abstract int Value
        {
            get;
        }

        public abstract byte[] Data
        {
            set;
        }

        public String Description
        {
            get
            {
                return description;
            }
        }

        public ushort ReportID
        {
            get
            {
                return reportID;
            }
        }

        public ushort Offset
        {
            get
            {
                return offset;
            }
        }

        public ushort Size
        {
            get
            {
                return size;
            }
        }

        /**
         * The description of where this report exists.
         */
        private readonly ushort reportID;
        private readonly ushort offset;
        private readonly ushort size;
        private readonly String description;

        /**
         * Default constructor, private to ensure the report description is included.
         */
        private UPSPowerEntry()
            : this(0, 0, 0, "Invalid")
        {
        }
    }; 

    class UPSPowerEntryBool : UPSPowerEntry
    {
        public UPSPowerEntryBool(ushort reportID, ushort offset, ushort size, String description)
            : base(reportID, offset, size, description)
        {
            if (size != 1)
            {
                throw new ArgumentException("Bool-type power entry cannot be made with a size other than '1'", "size");
            }

            /* Initialize internally to false. */
            field_val = false;
        }

        public override String ToString()
        {
            return field_val.ToString();
        }

        public override int Value
        {
            get
            {
                return field_val ? 1 : 0;
            }
        }

        public override byte[] Data
        {
            set
            {
                int byte_pos = this.Offset / 8;
                byte mask = (byte)(0x01 << this.Offset % 8);
                field_val = (value[byte_pos + 1] & mask) != 0;
            }
        }

        private UPSPowerEntryBool()
            : this(0, 0, 0, "Invalid")
        {
        }

        private bool field_val;
    };

    class UPSPowerEntryInt : UPSPowerEntry
    {
        public UPSPowerEntryInt(ushort reportID, ushort offset, ushort size, String description, String units)
            : base(reportID, offset, size, description)
        {
            this.units = units;

            /* Make sure the offset is byte-aligned. */
            if (offset % 8 != 0)
            {
                throw new ArgumentException("Int-type power entry must have a byte-aligned offset.", "offset");
            }
            if (size % 8 != 0)
            {
                throw new ArgumentException("Int-type power entry must have a byte-aligned size.", "size");
            }
            if (size > 32)
            {
                throw new ArgumentException("Int-type power entry cannot be larger than 4 bytes.", "size");
            }

            /* Initialize our internal value to 0. */
            field_val = 0;
        }

        public override string ToString()
        {
            return field_val.ToString() + units;
        }

        public override int Value
        {
            get
            {
                return field_val;
            }
        }

        public override byte[] Data
        {
            set
            {
                int start_pos = (this.Offset / 8) + 1;
                int cur_pos = start_pos + (this.Size / 8) - 1;
                field_val = 0;

                while (cur_pos >= start_pos)
                {
                    field_val <<= 8;
                    field_val |= value[cur_pos--];
                }
            }
        }

        private int field_val;
        private String units;
    };

    class CyberPowerData
    {
        public CyberPowerData()
        {
            upsDataEvent = new ManualResetEvent(false);
        }

        public async void RefreshData()
        {
            /* Ensure the descriptors are properly sorted. */
            Array.Sort(CyberPowerDesc);

            string selector = Windows.Devices.HumanInterfaceDevice.HidDevice.GetDeviceSelector(USB_POWER_PAGE, USB_POWER_UPS_ID, CYBER_POWER_VID, CYBER_POWER_PID);

            // Enumerate devices using the selector.
            var devices = await DeviceInformation.FindAllAsync(selector);

            if (devices.Any())
            {
                // Open the target HID device.
                HidDevice device =
                    await HidDevice.FromIdAsync(devices.ElementAt(0).Id, FileAccessMode.Read);

                if (device != null)
                {
                    int desc_num = 0;

                    while (desc_num < CyberPowerDesc.Length)
                    {
                        ushort reportID = CyberPowerDesc[desc_num].ReportID;
                        HidInputReport inputReport = null;

                        inputReport = await device.GetInputReportAsync(reportID);
                        IBuffer buffer = inputReport.Data;
                        byte[] my_bytes = new byte[buffer.Length];

                        DataReader d = DataReader.FromBuffer(buffer);
                        d.ReadBytes(my_bytes);
                        
                        while ((desc_num < CyberPowerDesc.Length) && (CyberPowerDesc[desc_num].ReportID == reportID))
                        {
                            CyberPowerDesc[desc_num++].Data = my_bytes;
                        }
                    }
                } 
                else
                {
                    throw new Exception("No devices found.");
                }
            }
            else
            {
                throw new Exception("No devices found.");
            }

            upsDataEvent.Set();
        }

        public override string ToString()
        {
            String info = "";

            foreach (UPSPowerEntry e in CyberPowerDesc)
            {
                info += e.Description + ": " + e.ToString() + "\n";
            }

            return info;
        }

        private UPSPowerEntry[] CyberPowerDesc = new UPSPowerEntry[]
        {
            new UPSPowerEntryInt(0x08, 0, 8, "Remaining Capacity", "%"),                /* Power Summary Group */
            new UPSPowerEntryInt(0x08, 8, 16, "Run Time To Empty", "s"),                /* Power Summary Group */
            new UPSPowerEntryInt(0x08, 24, 16, "Remaining Time Limit", "s"),            /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 0, 1, "A/C Present"),                           /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 1, 1, "Charging"),                              /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 2, 1, "Discharging"),                           /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 3, 1, "Below Remaining Capacity Limit"),        /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 4, 1, "Fully Charged"),                         /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 5, 1, "Remaining Time Limit Expired"),          /* Power Summary Group */
            new UPSPowerEntryInt(0x0c, 0, 8, "Audible Alarm Control", ""),              /* Power Summary Group */
            new UPSPowerEntryInt(0x10, 0, 16, "Low Voltage Transfer", "VAC"),           /* Input Group */
            new UPSPowerEntryInt(0x10, 16, 16, "High Voltage Transfer", "VAC"),         /* Input Group */
            /* The following group values exist but are worthless for our purposes. */
            //new UPSPowerEntryInt(0x14, 0, 8, "Test"),                                 /* Output Group */
            //new UPSPowerEntryInt(0x1a, 0, 8, "ff010043")                              /* Output Group */
        };

        const ushort CYBER_POWER_VID = 0x0764;
        const ushort CYBER_POWER_PID = 0x0501;
        const ushort USB_POWER_PAGE = 0x0084;
        const ushort USB_POWER_UPS_ID = 0x0004;

        public ManualResetEvent upsDataEvent;
    }

    class Program
    {
        static bool ParseArduinoData(SerialPort p, String command, float[] data)
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

        static bool QueryTiltData(SerialPort p, out float x, out float y, out float z)
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

        static bool QueryAmbientData(SerialPort p, out float temp, out float pressure, out float humidity)
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

        static bool QueryFanRate(SerialPort p, out float fan_rate)
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

        static bool InitTiltSensor(SerialPort p)
        {
            p.Write("init_bno055\n");
            return p.ReadLine().Trim().CompareTo("OK") == 0;
        }

        static bool InitAmbientSensor(SerialPort p)
        {
            p.Write("init_bme280\n");
            return p.ReadLine().Trim().CompareTo("OK") == 0;
        }

        static IRestResponse ExecuteRestRequest(IRestClient client, RestRequest request)
        {
            client.Timeout = 50;
            return client.Execute(request);
        }

        static bool QuerySGPDevice(string device, out SgpDeviceResponse resp)
        {
            IRestClient client = new RestClient();
            var request = new RestRequest(Method.POST)
            {
                Resource = _sgp_uri_base + "devicestatus",
                RequestFormat = DataFormat.Json
            };
            request.AddHeader("Accept", "application/json");

            SgpDeviceStatusRequest deviceStatusRequest = new SgpDeviceStatusRequest() { Device = device };
            request.AddJsonBody(deviceStatusRequest);

            IRestResponse response = ExecuteRestRequest(client, request);
            resp = JsonConvert.DeserializeObject<SgpDeviceResponse>(response.Content);

            return resp != null;
        }

        static bool QuerySGPScopePosition(out SgpTelescopePosResponse resp)
        {
            IRestClient client = new RestClient();
            var request = new RestRequest(Method.POST)
            {
                Resource = _sgp_uri_base + "telescopepos",
                RequestFormat = DataFormat.Json
            };
            request.AddHeader("Accept", "application/json");

            IRestResponse response = ExecuteRestRequest(client, request);
            resp = JsonConvert.DeserializeObject<SgpTelescopePosResponse>(response.Content);

            return resp != null;
        }

        public class SgGenericResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        public class SgpTelescopePosResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public double Ra { get; set; }
            public double Dec { get; set; }
        }

        public class SgpDeviceStatusRequest
        {
            public string Device { get; set; }
        }

        public class SgpDeviceResponse
        {
            /* "IDLE", "CAPTURING", "SOLVING", "BUSY", "MOVING", "DISCONNECTED", "PARKED" */
            public string State { get; set; }
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        public static void QueryRigRunner(out RigRunnerStatus rrStat)
        {
            rrStat = new RigRunnerStatus();
            WebRequest req = WebRequest.Create(_rr_base_uri + "status.xml");

            try
            {
                WebResponse res = req.GetResponse();
                Stream dataStream = res.GetResponseStream();
                StreamReader reader = new StreamReader(dataStream);
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(reader.ReadToEnd());
                reader.Close();
                res.Close();

                XmlNode node = xml.SelectSingleNode("rr4005i/SUPPLY");
                rrStat.Supply = Single.Parse(node.InnerText);

                node = xml.SelectSingleNode("rr4005i/RAILENA0");
                rrStat.Rail0_enabled = (Int32.Parse(node.InnerText) != 0);

                node = xml.SelectSingleNode("rr4005i/RAILENA1");
                rrStat.Rail1_enabled = (Int32.Parse(node.InnerText) != 0);

                node = xml.SelectSingleNode("rr4005i/RAILENA2");
                rrStat.Rail2_enabled = (Int32.Parse(node.InnerText) != 0);

                node = xml.SelectSingleNode("rr4005i/RAILENA3");
                rrStat.Rail3_enabled = (Int32.Parse(node.InnerText) != 0);

                node = xml.SelectSingleNode("rr4005i/RAILENA4");
                rrStat.Rail4_enabled = (Int32.Parse(node.InnerText) != 0);

                node = xml.SelectSingleNode("rr4005i/RAILLOAD0");
                rrStat.Rail0_load = Single.Parse(node.InnerText);

                node = xml.SelectSingleNode("rr4005i/RAILLOAD1");
                rrStat.Rail1_load = Single.Parse(node.InnerText);

                node = xml.SelectSingleNode("rr4005i/RAILLOAD2");
                rrStat.Rail2_load = Single.Parse(node.InnerText);

                node = xml.SelectSingleNode("rr4005i/RAILLOAD3");
                rrStat.Rail3_load = Single.Parse(node.InnerText);

                node = xml.SelectSingleNode("rr4005i/RAILLOAD4");
                rrStat.Rail4_load = Single.Parse(node.InnerText);
            }
            catch (Exception)
            {
                /* Not a great thing if the power supply is off! */
                rrStat = null;
            }
        }

        public class RigRunnerStatus
        {
            public float Supply { get; set; }
            public float Rail0_load { get; set; }
            public float Rail1_load { get; set; }
            public float Rail2_load { get; set; }
            public float Rail3_load { get; set; }
            public float Rail4_load { get; set; }
            public bool Rail0_enabled { get; set; }
            public bool Rail1_enabled { get; set; }
            public bool Rail2_enabled { get; set; }
            public bool Rail3_enabled { get; set; }
            public bool Rail4_enabled { get; set; }
        }

        static async void RigRunnerTest(String cmd)
        {
            var responseString = await _rig_runner_client.GetStringAsync(_rr_base_uri + "?" + cmd);
            _rrDataEvent.Set();
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
            if (!InitTiltSensor(p))
            {
                Console.WriteLine("FATAL: Could not initialize the tilt sensor!");
                p.Close();
                return;
            }

            if (!InitAmbientSensor(p))
            {
                Console.WriteLine("FATAL: Could not initialize the ambient sensor!");
                p.Close();
                return;
            }

            Console.WriteLine("RigRunner Test 1...");
            RigRunnerTest("RAILENA2=1");
            _rrDataEvent.WaitOne();
            Thread.Sleep(1000);
            Console.WriteLine("RigRunner Test 2...");
            RigRunnerTest("RAILENA2=0");
            _rrDataEvent.WaitOne();
            Thread.Sleep(1000);

            for (int i = 0; i < 100; i++)
            {
                /* Start a query of the UPS data. */
                ups.RefreshData();
                /* FIXME: This isn't correct (obviously); should move UPS stuff to a class and save state. */
                bool validUPS = true;

                /* Query all sensor data. */
                bool validTilt = QueryTiltData(p, out float x, out float y, out float z);
                bool validAmbient = QueryAmbientData(p, out float temp, out float pressure, out float humidity);
                bool validFan = QueryFanRate(p, out float tach_rate);

                /* Ping SGP for data. */
                QuerySGPDevice("Telescope", out SgpDeviceResponse sg_telescope_resp);
                QuerySGPDevice("Camera", out SgpDeviceResponse sg_camera_resp);
                QuerySGPDevice("FilterWheel", out SgpDeviceResponse sg_fw_resp);
                QuerySGPDevice("Focuser", out SgpDeviceResponse sg_focuser_resp);
                QuerySGPScopePosition(out SgpTelescopePosResponse sg_position_resp);

                QueryRigRunner(out RigRunnerStatus rr_status);

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
                    Console.WriteLine("Pressure (at altitude): {0:0.00} inHg", pascalsToInches(pressure));
                    Console.WriteLine("Pressure (sea level): {0:0.00} inHg", pascalsToInches(calcSeaPressure(temp, pressure, _altitude)));
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
                }
                else
                {
                    Console.WriteLine("WARNING: Camera query failed!");
                }
                if ((sg_fw_resp != null) && sg_fw_resp.Success)
                {
                    Console.WriteLine("Filter Wheel: " + sg_fw_resp.Message);
                }
                else
                {
                    Console.WriteLine("WARNING: Filter wheel query failed!");
                }
                if ((sg_focuser_resp != null) && sg_focuser_resp.Success)
                {
                    Console.WriteLine("Focuser: " + sg_focuser_resp.Message);
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
        private const String _sgp_uri_base = "http://localhost:59590/";
        private const String _rr_base_uri  = "http://172.20.0.157/";
        private const int    _altitude = 154;
        private static readonly HttpClient _rig_runner_client = new HttpClient();
        private static ManualResetEvent _rrDataEvent = new ManualResetEvent(false);
    }
}
