using System;
using System.Linq;
using System.Collections.Generic;

using SmartPackager;

namespace EMI
{
    using NGC;
    using Indicators;
    using MyException;
    using RPCInternal;
    /// <summary>
    /// �������� �� ��������������� �������� �������� ��� ������������ ������
    /// </summary>
    public partial class RPC
    {
        /// <summary>
        /// �������� � ���� ��� �� ������� �������
        /// </summary>
        /// <param name="array">�������������� ����� ������</param>
        /// <returns></returns>
        internal delegate IRPCReturn MicroFunc(INGCArray array);
        /// <summary>
        /// ��������� ����� �������� ���������� ��������� ���������
        /// </summary>
        /// <param name="sendClient">����� ������ ����� ���������� ���������</param>
        /// <returns></returns>
        public delegate Client[] ForwardingInfo(Client sendClient);
        /// <summary>
        /// ������������������ ������� - ������������ ��� ������
        /// </summary>
        private readonly Dictionary<int, MicroFunc> RegisteredMethods = new Dictionary<int, MicroFunc>();
        private readonly Dictionary<int, ForwardingInfo> RegisteredForwarding = new Dictionary<int, ForwardingInfo>();

#if DEBUG
        private readonly Dictionary<int, (string, int)> RegisteredMethodsName = new Dictionary<int, (string, int)>();
        private readonly Dictionary<int,string> RegisteredForwardingName = new Dictionary<int, string>();
#endif

#if DEBUG
        /// <summary>
        /// �������� ������ ������������������ �������
        /// </summary>
        /// <returns></returns>
        public KeyValuePair<int,(string,int)>[] GetRegisteredMethodsName()
        {
            lock (this)
            {
                return RegisteredMethodsName.ToArray();
            }
        }

        /// <summary>
        /// �������� ������ ������������������ ������� (Forwarding)
        /// </summary>
        /// <returns></returns>
        public KeyValuePair<int, string>[] GetRegisteredForwardingName()
        {
            lock (this)
            {
                return RegisteredForwardingName.ToArray();
            }
        }
#endif

#if DEBUG
        /// <summary>
        /// ���������� ����� ������� ������ ������������������ �������
        /// </summary>
        public event Action OnChangedRegisteredMethods;
        /// <summary>
        /// ���������� ����� ������� ������ ������������������ ������� (Forwarding)
        /// </summary>
        public event Action OnChangedRegisteredMethodsForwarding;
#endif
        /// <summary>
        /// ��������� ��� HeadlessHandler, � ��������� ������� ����������� �������� �� ���������
        /// </summary>
        public RPC()
        {
        }

        /// <summary>
        /// ���������� ������ � ����������� � ������ (��� + ID � DEBUG, ������ ID � Release)
        /// </summary>
        internal string GetMethodInfo(int ID)
        {
#if DEBUG
            lock (this)
            {
                if (RegisteredMethodsName.TryGetValue(ID, out var info))
                    return $"{info.Item1} (ID: {ID})";
            }
#endif
            return $"ID: {ID}";
        }

        /// <summary>
        /// ���������� ������ ������������������ ������� ��� �����������
        /// </summary>
        internal string GetRegisteredMethodsList()
        {
#if DEBUG
            lock (this)
            {
                if (RegisteredMethodsName.Count == 0)
                    return "(�����)";
                return string.Join(", ", RegisteredMethodsName.Values.Select(v => v.Item1));
            }
#else
            return "(�������� ������ � DEBUG)";
#endif
        }

        /// <summary>
        /// �������� ���������� ��� ������ RPC ������
        /// </summary>
        internal static void LogRPCException(AIndicator indicator, Exception e)
        {
#if DEBUG
            Console.WriteLine($"EMI RPC => Exception in [{indicator.Name}] (ID: {indicator.ID}): {e}");
#else
            Console.WriteLine($"EMI RPC => Exception (ID: {indicator.ID}): {e}");
#endif
        }

        /// <summary>
        /// �������� �������� ������� �� ���� - ���� �� ���������� ������ null (������-���������)
        /// </summary>
        /// <param name="ID"></param>
        /// <returns></returns>
        internal MicroFunc TryGetRegisteredMethod(int ID)
        {
            lock (this)
            {
                RegisteredMethods.TryGetValue(ID, out var fun);
                return fun;
            }
        }

        /// <summary>
        /// �������� �������� ������� �� ���� - ���� �� ���������� ������ null (������-���������)
        /// </summary>
        /// <param name="ID"></param>
        /// <returns></returns>
        internal ForwardingInfo TryGetRegisteredForwarding(int ID)
        {
            lock (this)
            {
                RegisteredForwarding.TryGetValue(ID, out var fun);
                return fun;
            }
        }

        /// <summary>
        /// ���������� �������� ����������� ������ (������� ������ �������� ������������)
        /// </summary>
        /// <param name="indicator"></param>
        /// <param name="micro"></param>
        /// <returns></returns>
        private IRPCRemoveHandle RegisterMethodHelp(AIndicator indicator, MicroFunc micro)
        {
            int id = indicator.ID;
            lock (this)
            {
                if (RegisteredMethods.ContainsKey(id))
                {
                    RegisteredMethods[id] += micro;
#if DEBUG
                    var dat = RegisteredMethodsName[id];
                    RegisteredMethodsName[id] = (dat.Item1, dat.Item2 + 1);
                    OnChangedRegisteredMethods?.Invoke();
#endif
                }
                else
                {
                    RegisteredMethods.Add(id, micro);
#if DEBUG
                    RegisteredMethodsName.Add(id, (indicator.Name,1));
                    OnChangedRegisteredMethods?.Invoke();
#endif
                }
                return new RemoveHandleMethod(id, micro, this);
            }
        }

        /// <summary>
        /// ������������ ����� ��������� �������
        /// </summary>
        /// <param name="indicator">������ �� ������� ��� ������ �� ��������� ��������</param>
        /// <param name="info">������� ����� ���������� ��� ��������� ��������� � ������ ������� ������ �������� ������� ���������� ��������� ���������</param>
        /// <returns></returns>
        public IRPCRemoveHandle RegisterForwarding(AIndicator indicator, ForwardingInfo info)
        {
            lock (this)
            {
                if (RegisteredForwarding.ContainsKey(indicator.ID))
                {
                    throw new AlreadyException("Forwarding has already been registered at this indicator");
                }

                RegisteredForwarding.Add(indicator.ID, info);
#if DEBUG
                RegisteredForwardingName.Add(indicator.ID, indicator.Name);
                OnChangedRegisteredMethodsForwarding?.Invoke();
#endif
                return new RemoveHandleForwarding(indicator.ID, this);
            }
        }

// RegisterMethod overloads moved to RPC.Generated.cs (generated from RPC.Generated.tt)

        /// <summary>
        /// ��������� ������� ������������������ �����
        /// </summary>
        public class RemoveHandleMethod : IRPCRemoveHandle
        {
            private readonly int ID;
            private RPC RPC;
            private MicroFunc Micro;
            private bool IsRemoved = false;

            internal RemoveHandleMethod(int id, MicroFunc micro, RPC rpc)
            {
                ID = id;
                Micro = micro;
                RPC = rpc;
            }

            /// <summary>
            /// ������� ����� �� ������ ������������������ (��� ������ ������ ����� �������)
            /// </summary>
            public void Remove()
            {
                lock (RPC)
                {
                    if (IsRemoved)
                        throw new AlreadyException();
                    IsRemoved = true;

                    var deleg = RPC.RegisteredMethods[ID];
                    if (deleg.GetInvocationList().GetLength(0) > 1)
                    {
                        deleg -= Micro;
#if DEBUG
                        var dat = RPC.RegisteredMethodsName[ID];
                        RPC.RegisteredMethodsName[ID] = (dat.Item1, dat.Item2 - 1);
                        RPC.OnChangedRegisteredMethods?.Invoke();
#endif
                    }
                    else
                    {
                        RPC.RegisteredMethods.Remove(ID);
#if DEBUG
                        RPC.RegisteredMethodsName.Remove(ID);
                        RPC.OnChangedRegisteredMethods?.Invoke();
#endif
                    }

                    RPC = null;
                    Micro = null;

                }
            }
        }

        /// <summary>
        /// ��������� ������� ������������������ �����
        /// </summary>
        public class RemoveHandleForwarding : IRPCRemoveHandle
        {
            private readonly int ID;
            private RPC RPC;
            private bool IsRemoved = false;

            internal RemoveHandleForwarding(int id, RPC rpc)
            {
                ID = id;
                RPC = rpc;
            }

            /// <summary>
            /// ������� ����� �� ������ ������������������ (��� ������ ������ ����� �������)
            /// </summary>
            public void Remove()
            {
                lock (RPC)
                {
                    if (IsRemoved)
                        throw new AlreadyException();
                    IsRemoved = true;

                    RPC.RegisteredForwarding.Remove(ID);
#if DEBUG
                    RPC.RegisteredForwardingName.Remove(ID);
                    RPC.OnChangedRegisteredMethodsForwarding?.Invoke();
#endif
                    RPC = null;
                }
            }
        }

        /// <summary>
        /// ��������� �������� ��� ������ � ������ ��� �� ������� ��� ������ ����� ��� ����� �����
        /// </summary>
        public class RemoveHandleGroup
        {
            private readonly HashSet<IRPCRemoveHandle> Handles = new HashSet<IRPCRemoveHandle>();

            /// <summary>
            /// �������� ����� � ������ ��� ��������
            /// </summary>
            /// <param name="handle">�����</param>
            public void Add(IRPCRemoveHandle handle)
            {
                Handles.Add(handle);
            }

            /// <summary>
            /// ������� ��� ����������� ������
            /// </summary>
            public void RemoveAll()
            {
                foreach(var handle in Handles)
                {
                    handle.Remove();
                }
                Handles.Clear();
            }
        }
    }
}