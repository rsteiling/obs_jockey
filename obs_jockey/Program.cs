using System;
using System.IO.Ports;
using System.Threading;
using System.Linq;

using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;
using Windows.Storage.Streams;

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
        public UPSPowerEntryInt(ushort reportID, ushort offset, ushort size, String description)
            : base(reportID, offset, size, description)
        {
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
            return field_val.ToString();
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
            new UPSPowerEntryInt(0x08, 0, 8, "Remaining Capacity"),                     /* Power Summary Group */
            new UPSPowerEntryInt(0x08, 8, 16, "Run Time To Empty"),                     /* Power Summary Group */
            new UPSPowerEntryInt(0x08, 24, 16, "Remaining Time Limit"),                 /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 0, 1, "A/C Present"),                           /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 1, 1, "Charging"),                              /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 2, 1, "Discharging"),                           /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 3, 1, "Below Remaining Capacity Limit"),        /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 4, 1, "Fully Charged"),                         /* Power Summary Group */
            new UPSPowerEntryBool(0x0b, 5, 1, "Remaining Time Limit Expired"),          /* Power Summary Group */
            new UPSPowerEntryInt(0x0c, 0, 8, "Audible Alarm Control"),                  /* Power Summary Group */
            new UPSPowerEntryInt(0x10, 0, 16, "Low Voltage Transfer"),                  /* Input Group */
            new UPSPowerEntryInt(0x10, 16, 16, "High Voltage Transfer"),                /* Input Group */
            new UPSPowerEntryInt(0x14, 0, 8, "Test"),                                   /* Output Group */
            new UPSPowerEntryInt(0x1a, 0, 8, "ff010043")                                /* Output Group */
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

        static void Main(string[] args)
        {
            CyberPowerData ups = new CyberPowerData();
            
            SerialPort p = new SerialPort("COM7", 115200, Parity.None, 8, StopBits.One);      
            p.Handshake = Handshake.None;
            p.RtsEnable = true;
            p.ReadTimeout = SerialPort.InfiniteTimeout;
            p.Open();

            float x, y, z;
            float temp, pressure, humidity;
            float tach_rate;

            while (true)
            {
                ups.RefreshData();

                QueryTiltData(p, out x, out y, out z);
                QueryAmbientData(p, out temp, out pressure, out humidity);
                QueryFanRate(p, out tach_rate);

                ups.upsDataEvent.WaitOne();

                Console.WriteLine("*********");

                Console.WriteLine(ups.ToString());

                Console.WriteLine("Temperature: {0:0.00}°C", temp);
                Console.WriteLine("Pressure: {0:0.00} Pa", pressure);
                Console.WriteLine("Humidity: {0:0.00}%", humidity);

                Console.WriteLine("X: {0:0.00}°", x);
                Console.WriteLine("Y: {0:0.00}°", y);
                Console.WriteLine("Z: {0:0.00}°", z);

                Console.WriteLine("Fan rate: {0:0}%", tach_rate);

                Console.WriteLine("*********");

                Console.WriteLine("");

                Thread.Sleep(2000);
            }

            p.Close();
        }
    }
}
