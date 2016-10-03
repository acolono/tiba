using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using M2Mqtt;
using M2Mqtt.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TmaIotboxAdapter
{
    public class Program
    {
        public static string MqttClientId = Guid.NewGuid().ToString();
        public static void Main(string[] args)
        {
            while (true)
            {
                try
                {
                    Task.Run(AsyncMain).Wait();

                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.ToString());
                }
                System.Threading.Thread.Sleep(1000);
            }

        }

        public static string e(string key, string defaultValue = null)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value)) value = defaultValue;
            return value;
        }


        private static async Task AsyncMain()
        {
            Console.WriteLine("Connect");

            var m = new MqttClient(e("mqtt_host","mqtt"), int.Parse(e("mqtt_port","1883")), false, null, null, MqttSslProtocols.None);
            m.Connect(MqttClientId);
            var cid = e("api_cid");
            var sid = e("api_sid");

            var selects = new List<string>()
            {
                //"GetPosCmdCnt",
                //"GetPosCmdFin",
                //"BLEModeCmdCnt",
                //"BLEMode",
                //"LedRGBCmdCnt",
                //"LedRGBValue",
                //"ActiveModeCmdCnt",
                //"ActiveMode",
                //"ActiveModeTimer",
                //"AccuState",
                //"Batt",
                "Temperature",
                "RSSI",
                "BtnState",
                "Position"
            };

            var apiSleep = int.Parse(e("api_sleep", "1000"));
            var apiSessionTime = int.Parse(e("api_sessionTime", "3600000"));
            var includeResponseTime = bool.Parse(e("api_responseTime", "false"));
            var topicBase = e("mqtt_topicBase", "/opentrigger/tmaIot/").TrimEnd('/');
            var logPayload = bool.Parse(e("api_log", "false"));

            var sessWatch = Stopwatch.StartNew();



            using (var c = new ApiLib(e("api_user"), e("api_pass"), e("api_cid"), e("api_sid")))
            while (true)
            {
                var tasks = new List<Task<ushort>>();
                var sw = new Stopwatch();
                foreach (var @select in selects)
                {
                    sw.Restart();
                    var v = await (select == "Position" ? c.GetPosition() : c.GetHistData(0, select));
                    sw.Stop();
                    v.Add("cid",cid);
                    v.Add("sid",sid);
                    if(includeResponseTime) v.Add("ApiResponseTime", sw.ElapsedMilliseconds);

                    var topic = $"{topicBase}/{cid}/{sid}/{select}";
                    v.Add("topic", topic);
                    var json = JsonConvert.SerializeObject(v, Formatting.Indented);

                    if (logPayload) Console.WriteLine(json);

                    tasks.Add(m.PublishAsync(topic, Encoding.UTF8.GetBytes(json)));
                }
                if (sessWatch.ElapsedMilliseconds > apiSessionTime) break;
                System.Threading.Thread.Sleep(apiSleep);
                await Task.WhenAll(tasks);

            }
            m.Disconnect();
        }
    }

    public static class MqttExtensions
    {
        public static async Task<ushort> PublishAsync(this MqttClient client, string topic, byte[] payload)
        {
            return await Task.Run(() => client.Publish(topic, payload, MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false));
        }
    }


    public class ApiLib : IDisposable
    {
        private readonly string _cid;
        private readonly string _sid;

        /// <summary>
        /// no trailing slash
        /// </summary>
        private const string BaseUrl = "https://iot-box.microtronics.at/api/1";

        private HttpClient _client = null;

        public ApiLib(string username, string password, string cid, string sid)
        {
            _cid = cid;
            _sid = sid;
            var credBytes = Encoding.ASCII.GetBytes(username + ':' + password);
            var credBase64 = Convert.ToBase64String(credBytes);
            var authHeader = new AuthenticationHeaderValue("Basic", credBase64);
            _client = new HttpClient(
                //new HttpClientHandler { Proxy = new FiddlerProxy(), UseProxy = true }
            );
            _client.DefaultRequestHeaders.Authorization = authHeader;

        }


        private class GetHistDataRequest
        {
            public GetHistDataRequest(string select)
            {
                this.select = new[] {select};
            }

            public string[] select { get; set; }
        }

        public async Task<Dictionary<string, object>> GetHistData(int nr, string select)
        {
            var reqData = new GetHistDataRequest(select);
            var reqString = JsonConvert.SerializeObject(reqData);
            var url = $"{BaseUrl}/customers/{_cid}/sites/{_sid}/histdata{nr}/youngest?json={reqString}";
            string responseText = null;

            using (var msg = new HttpRequestMessage(HttpMethod.Get, url))
            {

                using (var response = await _client.SendAsync(msg))
                {
                    var result = await response.Content.ReadAsByteArrayAsync();
                    responseText = Encoding.UTF8.GetString(result);
                }
            }
            if (string.IsNullOrWhiteSpace(responseText))
                throw new ArgumentOutOfRangeException($"Invalid response to: {url}");
            var ja = JArray.Parse(responseText);
            var dict = new Dictionary<string, object>
            {
                {"Type", select},
                {"Timestamp", ParseFunnyTimestamp((string) ja[0][0])},
                {select, ja[0][1]}
            };

            return dict;
        }

        public async Task<Dictionary<string, object>> GetPosition()
        {
            var url = $"{BaseUrl}/customers/{_cid}/sites/{_sid}/pos/youngest";
            string responseText = null;

            using (var response = await _client.GetAsync(url))
            {
                var result = await response.Content.ReadAsByteArrayAsync();
                responseText = Encoding.UTF8.GetString(result);
            }

            if (string.IsNullOrWhiteSpace(responseText))
                throw new ArgumentOutOfRangeException($"Invalid response to: {url}");
            var ja = JArray.Parse(responseText);
            var dict = new Dictionary<string, object>
            {
                {"Type", "Position"},
                {"Timestamp", ParseFunnyTimestamp((string) ja[0][0])},
                {"Lat", ja[0][1]},
                {"Lon", ja[0][2]}
            };
            try { dict.Add("UnknownValue", ja[0][3]); } catch  { /* undocumented shit... dont care */ }

            return dict;
        }

        public void Dispose()
        {
            _client?.Dispose();
            _client = null;
        }


        private static readonly string[] TimestampFormats = new[] { "yyyyMMddHHmmssfffzzz", "yyyyMMddHHmmssffzzz" };
        private DateTimeOffset ParseFunnyTimestamp(string timestamp)
        {
            var withTz = timestamp + "+0000";
            return DateTimeOffset.ParseExact(withTz, TimestampFormats, CultureInfo.InvariantCulture,DateTimeStyles.None).ToLocalTime();
        }
    }

}
