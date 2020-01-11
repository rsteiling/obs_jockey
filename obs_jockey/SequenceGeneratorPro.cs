using System;

using RestSharp;
using Newtonsoft.Json;

namespace ObsJockey
{
    class SequenceGeneratorPro
    {
        public static IRestResponse ExecuteRestRequest(IRestClient client, RestRequest request)
        {
            client.Timeout = 50;
            return client.Execute(request);
        }

        public static bool QuerySGPDevice(string device, out SgpDeviceResponse resp)
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

        public static bool QuerySGPScopePosition(out SgpTelescopePosResponse resp)
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

        private const String _sgp_uri_base = "http://localhost:59590/";
    }
}
