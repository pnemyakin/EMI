using System;
using System.Collections.Generic;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Linq;
using EMI.Network.NetTCPV3;

namespace EMI.Debugger
{
    using EMI.DebugServer;

    internal static class EMIDClient
    {
        public static RecordedData Data = null!;
        public static Client Client = new(NetTCPV3Service.Service);
        public static event Action? Connect;
        public static event Action? Disconnect;
        public static event Action? OnDataGet;
        private static readonly List<IRPCRemoveHandle> HandlesRPC = new(50);

        static EMIDClient()
        {
            System.Timers.Timer timer = new(100);
            timer.Elapsed += DataGetTimer;
            timer.Start();
            HandlesRPC.Add(Client.RPC.RegisterMethod((DebuggerType type) =>
            {
                Data = new RecordedData
                {
                    Type = type
                };
                switch (type)
                {
                    case DebuggerType.Server:
                        HandlesRPC.Add(Client.RPC.RegisterMethod(datas =>
                        {
                            foreach (var data in datas)
                            {
                                var client = GetDClient(data.ClientID);
                                client.RPC = data;
                            }
                        }, EDebuggerServerRC.OnSendRPCInfo));

                        HandlesRPC.Add(Client.RPC.RegisterMethod(datas =>
                        {
                            foreach(var cf in Data.ClientsInfo.Values)
                            {
                                if(!Data.ClientsInfo.ContainsKey(cf.ClientInfo.ClientID))
                                    cf.ClientInfo.IsConnect = false;
                            }

                            foreach (var cft in datas.ClientInfos)
                            {
                                var cf = cft;
                                var client = GetDClient(cf.ClientID);
                                string address = client.ClientInfo.Address;
                                if(cf.Address==null || cf.Address.Length == 0)
                                {
                                    cf.Address = address;
                                    
                                }
                                client.ClientInfo = cf;
                            }

                            foreach (var rf in datas.RPCInfo)
                            {
                                var client = GetDClient(rf.ClientID);
                                client.RPC = rf;
                            }

                            Data.ServerInfo = datas.ServerInfo;
                        }, EDebuggerServerRC.OnSendAllInfo));
                        break;
                    case DebuggerType.Client:
                        HandlesRPC.Add(Client.RPC.RegisterMethod(data =>
                        {
                            var client = GetDClient(data.ClientID);
                            client.RPC = data;
                        }, EDebuggerClientRC.OnSendRPCInfo));

                        HandlesRPC.Add(Client.RPC.RegisterMethod(data =>
                        {
                            var client = GetDClient(data.ClientInfos.ClientID);
                            client.ClientInfo = data.ClientInfos;
                            client.RPC = data.RPCInfo;
                        }, EDebuggerClientRC.OnSendAllInfo));
                        break;
                    default:
                        throw new NotSupportedException("Неизвестный тип сервера, возможно сбой EMI");
                }
                Connect?.Invoke();
            }, EDebuggerBothRC.InitDebugger));

            HandlesRPC.Add(Client.RPC.RegisterMethod(msg =>
            {
                var client = GetDClient(msg.ClientID);
                client.Logs.Add(msg);
            }, EDebuggerBothRC.OnMSG));

            Client.Disconnected += Client_Disconnected;
        }

        private static readonly SemaphoreSlim semaphoreTimer = new(1, 1);
        private static async void DataGetTimer(object? source, ElapsedEventArgs e)
        {
            if (Client.IsConnect && Data != null)
            {
                await semaphoreTimer.WaitAsync();
                try
                {
                    Data.NGC.Add(await EDebuggerBothRC.GetNGCInfo.RCall(Client));

                    switch (Data.Type)
                    {
                        case DebuggerType.Server:
                            {
                                var clientInfos = await EDebuggerServerRC.GetClientInfos.RCall(Client);
                                foreach (var clientInfo in clientInfos)
                                {
                                    var client = GetDClient(clientInfo.ClientID);
                                    client.ClientInfo = clientInfo;
                                }

                                var srvInf = await EDebuggerServerRC.GetServerInfo.RCall(Client);
                                Data.ServerInfo = srvInf;
                            }
                            break;
                        case DebuggerType.Client:
                            {
                                var clientInfo = await EDebuggerClientRC.GetClientInfo.RCall(Client);
                                var client = GetDClient(clientInfo.ClientID);
                                client.ClientInfo = clientInfo;
                            }
                            break;
                    }

                    OnDataGet?.Invoke();
                }
                catch (Exception g)
                {
                    MessageBox.Show(g.ToString());
                    throw;
                }
                finally
                {
                    semaphoreTimer.Release();
                }
            }
        }

        private static DClient GetDClient(Guid guid)
        {
            lock (Data)
            {
                if (Data.ClientsInfo.TryGetValue(guid, out var client))
                {
                    return client;
                }
                else
                {
                    client = new DClient();
                    client.ClientInfo.ClientID = guid;
                    Data.ClientsInfo.Add(guid, client);
                    return client;
                }
            }
        }

        private static void Client_Disconnected(string error)
        {
            foreach (var handle in HandlesRPC)
            {
                handle.Remove();
            }
            HandlesRPC.Clear();
            Disconnect?.Invoke();
        }
    }
}
