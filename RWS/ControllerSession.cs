﻿using Newtonsoft.Json;
using RWS.Data;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using RWS.RobotWareServices;
using RWS.UserServices;
using RWS.SubscriptionServices;
using System.Globalization;
using System.Threading.Tasks;
using System.Net.Http;

namespace RWS
{
    public partial class ControllerSession
    {

        const string templateUri = "{0}/{1}";
        public string IP { get; private set; }
        public UAS UAS { get; private set; }
        public CookieContainer CookieContainer { get; set; } = new CookieContainer();
        public ControllerService ControllerService { get; set; }
        public RobotWareService RobotWareService { get; set; }
        public FileService FileService { get; set; }
        public UserService UserService { get; set; }
        public SubscriptionService SubscriptionService { get; set; }
        public ControllerSession(string controllerIP, [Optional]UAS uas)
        {
            IP = controllerIP;

            UAS = uas ?? new UAS("Default User", "robotics");


            ControllerService = new ControllerService(this);
            RobotWareService = new RobotWareService(this);
            FileService = new FileService(this);
            SubscriptionService = new SubscriptionService(this);
            UserService = new UserService(this);
        }

        public void Connect(string ip, UAS uas)
        {
            IP = ip;
            UAS = uas;
        }

        public GenResponse<T> Call<T>(string method, string domain, Tuple<string, string>[] dataParameters, Tuple<string, string>[] urlParameters)
        {

            var uri = string.Format(CultureInfo.InvariantCulture, templateUri, "http://" + IP, domain);

            if (uri.EndsWith("/", StringComparison.InvariantCulture)) uri = uri.TrimEnd('/');

            if (urlParameters != null && urlParameters.Length > 0)
            {
                StringBuilder extraParameters = new StringBuilder();

                foreach (var item in urlParameters)
                {
                    extraParameters.Append((extraParameters.Length == 0 ? "?" : "&") + item.Item1 + "=" + item.Item2);
                }

                if (extraParameters.Length > 0)
                {
                    uri += extraParameters.ToString();
                }
            }

            Debug.WriteLine(uri);

            if(method == "GET")
            {
                return CallWithJsonAsync<T>(new Uri(uri), method, dataParameters).Result; //blocking wait here
            }
            else
            {
                return CallWithJson<T>(new Uri(uri), method, dataParameters);
            }

        }

        public async Task<GenResponse<T>> CallWithJsonAsync<T>(Uri uri, string method, Tuple<string, string>[] dataParameters, params Tuple<string, string>[] headers)
        {
            HttpResponseMessage resp1;
            var method1 = new HttpMethod(method);
            using (var handler1 = new HttpClientHandler() {
                Credentials = new NetworkCredential(UAS.User, UAS.Password),
                CookieContainer = CookieContainer,
            })
            using (var client1 = new HttpClient(handler1))
            using (var requestMessage = new HttpRequestMessage(method1, uri))
            {
                foreach (var header in headers)
                {
                    requestMessage.Headers.Add(header.Item1, header.Item2);
                }
                requestMessage.Headers.Accept.ParseAdd("application/x-www-form-urlencoded");

                resp1 = await client1.SendAsync(requestMessage).ConfigureAwait(false);
            }

            using (var sr = new StreamReader(await resp1.Content.ReadAsStreamAsync().ConfigureAwait(false)))
            {
                var content = sr.ReadToEnd();
                GenResponse<T> jsonResponse = default;
                jsonResponse = JsonConvert.DeserializeObject<GenResponse<T>>(content);
                return jsonResponse;
            }
        }

        public GenResponse<T> CallWithJson<T>(Uri uri, string method, Tuple<string, string>[] dataParameters, params Tuple<string, string>[] headers)
        {
            var request = WebRequest.CreateHttp(uri);

            request.Credentials = new NetworkCredential(UAS.User, UAS.Password);

            if (CookieContainer != null)
                request.CookieContainer = CookieContainer;

            foreach (var header in headers)
            {
                request.Headers.Add(header.Item1, header.Item2);
            }

            request.Proxy = null;
            request.Method = method;
            //   request.PreAuthenticate = true;
            request.ContentType = "application/x-www-form-urlencoded";


            if (dataParameters != null && dataParameters.Length > 0 && method != "GET")
            {
                StringBuilder combinedParams = new StringBuilder();

                foreach (var item in dataParameters)
                {
                    combinedParams.Append((item.Item1 == dataParameters[0].Item1 ? "" : "&") + item.Item1 + "=" + item.Item2);
                }

                Stream stream = request.GetRequestStream();

                if (method == "PUT")
                {
                    using (FileStream fs = File.OpenRead(combinedParams.ToString().Split('=')[0]))
                    {
                        using (BinaryReader br = new BinaryReader(fs))
                        {
                            byte[] bb = br.ReadBytes((int)fs.Length);
                            stream.Write(bb, 0, bb.Length);
                        }
                    }
                }
                else
                    stream.Write(Encoding.ASCII.GetBytes(combinedParams.ToString()), 0, combinedParams.ToString().Length);

                stream.Close();
            }

            using (var httpResponse = (HttpWebResponse)request.GetResponse())
            {
                string cookieHeader = httpResponse.Headers[HttpResponseHeader.SetCookie];

                if (cookieHeader != null)
                {
                    CookieContainer.SetCookies(new Uri("http://" + IP), cookieHeader);
                }

                //if (httpResponse.StatusCode == HttpStatusCode.OK)
                //{

                using (var sr = new StreamReader(httpResponse.GetResponseStream()))
                {

                    var content = sr.ReadToEnd();

                    GenResponse<T> jsonResponse = default;

                    jsonResponse = JsonConvert.DeserializeObject<GenResponse<T>>(content);

                    return jsonResponse;

                }
                //   }

            }
        }

        private static string GetDebugCallDetails(string uri)
        {
            StringBuilder sb = new StringBuilder();
            var u = new Uri(uri);
            sb.Append(u.AbsolutePath);
            if (u.Query.StartsWith("?", StringComparison.InvariantCulture))
            {
                var queryParameters = u.Query.Substring(1).Split('&');
                foreach (var p in queryParameters)
                {

                    var kv = p.Split('=');
                    if (kv.Length == 2)
                    {
                        if (sb.Length != 0)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(kv[0]).Append(" = ").Append(kv[1]);
                    }

                }
            }
            return sb.ToString();
        }

    }

    public class UAS
    {
        public string User { get; set; }
        public string Password { get; set; }

        public UAS(string user, string password)
        {
            User = user;
            Password = password;
        }
    }



}
