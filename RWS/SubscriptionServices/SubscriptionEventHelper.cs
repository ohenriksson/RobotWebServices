﻿using RWS.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static RWS.SubscriptionServices.SubscriptionEventHelper;

namespace RWS.SubscriptionServices
{

    public abstract class SubscriptionEventHelper
    {
        public Dictionary<object, ClientWebSocket> SubscriptionSockets { get; } = new Dictionary<object, ClientWebSocket>();
        protected ValueChangedIOEventHandler ValueChangedEventHandler { get; set; }

        public delegate void ValueChangedIOEventHandler(object source, IOEventArgs args);
        private int Prio { get; set; } = 1;


        public async void StartSubscription(ControllerSession cs, string resource)
        {
            using (HttpClientHandler handler = new HttpClientHandler { Credentials = new NetworkCredential(cs?.UAS.User, cs?.UAS.Password) })
            {
                handler.Proxy = null;   // disable the proxy, the controller is connected on same subnet as the PC 
                handler.UseProxy = false;

                using (HttpClient client = new HttpClient(handler))
                {
                    using (CancellationTokenSource cancelToken = new CancellationTokenSource())
                    {
                        var httpContent = new Dictionary<string, string>
                        {
                            { "resources", "1" },
                            { "1", resource },
                            { "1-p", Prio.ToString(CultureInfo.InvariantCulture) }
                        };


                        await SocketThreadAsync(client, cs.IP, httpContent, cs.UAS, cancelToken.Token).ConfigureAwait(true);

                    }
                }
            }
        }

        private async Task SocketThreadAsync(HttpClient client, string ip, Dictionary<string, string> httpContent, UAS uas, CancellationToken cancelToken)
        {
            //post that you want to subscribe on values

            using (FormUrlEncodedContent fued = new FormUrlEncodedContent(httpContent))
            {
                var resp = await client.PostAsync(new Uri($"http://{ip}/subscription"), fued).ConfigureAwait(true);
                resp.EnsureSuccessStatusCode();

                //Get the ABB cookie, which will be used to connect to to the websocket
                var header = resp.Headers.FirstOrDefault(p => p.Key == "Set-Cookie");
                var val = header.Value.Last();
                string abbCookie = val.Split('=')[1].Split(';')[0];


                //Setup the websocket connection
                using (ClientWebSocket wSock = new ClientWebSocket())
                {
                    wSock.Options.Credentials = new NetworkCredential(uas.User, uas.Password);
                    wSock.Options.Proxy = null;
                    CookieContainer cc = new CookieContainer();
                    cc.Add(new Uri($"http://{ip}"), new Cookie("ABBCX", abbCookie, "/", ip));
                    wSock.Options.Cookies = cc;
                    wSock.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(5000);
                    wSock.Options.AddSubProtocol("robapi2_subscription");

                    //Connect
                    await wSock.ConnectAsync(new Uri($"ws://{ip}/poll"), cancelToken).ConfigureAwait(true);
                    var bArr = new byte[1024];
                    ArraySegment<byte> arr = new ArraySegment<byte>(bArr);

                    SubscriptionSockets.Add(ValueChangedEventHandler, wSock);

                    while (ValueChangedEventHandler != null)
                    {
                        try
                        {
                            var res = await wSock.ReceiveAsync(arr, cancelToken).ConfigureAwait(true);

                            if (ValueChangedEventHandler == null)
                                break;

                            //parse message
                            var s = Encoding.ASCII.GetString(arr.Array);

                            s = s.Split(new string[] { "lvalue\">" }, StringSplitOptions.None)[1].Split('<')[0].Trim();

                            ValueChangedEventHandler(this, new IOEventArgs() { LValue = int.Parse(s, CultureInfo.InvariantCulture) });

                        }
                        catch (Exception ex)
                        {
                            if (ex is WebSocketException && wSock.State == WebSocketState.Aborted)
                                break;
                        }
                    }
                }
            }

        }

    }
}
