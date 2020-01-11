using System;
using System.Net;
using System.Net.Http;
using System.Xml;
using System.IO;
using System.Threading;

namespace ObsJockey
{
    class RigRunner
    {
        public static async void RigRunnerTest(String cmd)
        {
            var responseString = await _rig_runner_client.GetStringAsync(_rr_base_uri + "?" + cmd);
            _rrDataEvent.Set();
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

        private const String _rr_base_uri = "http://172.20.0.157/";
        private static readonly HttpClient _rig_runner_client = new HttpClient();
        public static ManualResetEvent _rrDataEvent = new ManualResetEvent(false);
    }
}
