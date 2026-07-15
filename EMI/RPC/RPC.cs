using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EMI
{
    using Indicators;
    using MyException;
    using NGC;
    using RPCInternal;

    /// <summary>
    /// Класс для регистрации и обработки удалённых вызовов методов
    /// </summary>
    public partial class RPC
    {
        /// <summary>
        /// Делегат для вызова метода по сети без возврата значения
        /// </summary>
        /// <param name="array">Упакованный массив данных</param>
        /// <returns></returns>
        internal delegate ValueTask<IRPCReturn> MicroFunc(INGCArray array);
        /// <summary>
        /// Получить список клиентов, которым переслать удалённый вызов
        /// </summary>
        /// <param name="sendClient">Клиент, который сделал удалённый вызов</param>
        /// <returns></returns>
        public delegate Client[] ForwardingInfo(Client sendClient);
        /// <summary>
        /// Зарегистрированные методы - используется для вызова
        /// </summary>
        private readonly Dictionary<long, MicroFunc> RegisteredMethods = new Dictionary<long, MicroFunc>();
        private readonly Dictionary<long, ForwardingInfo> RegisteredForwarding = new Dictionary<long, ForwardingInfo>();

#if DEBUG
        private readonly Dictionary<long, (string, int)> RegisteredMethodsName = new Dictionary<long, (string, int)>();
        private readonly Dictionary<long, string> RegisteredForwardingName = new Dictionary<long, string>();
#endif

#if DEBUG
        /// <summary>
        /// Получить список зарегистрированных методов
        /// </summary>
        /// <returns></returns>
        public KeyValuePair<long,(string,int)>[] GetRegisteredMethodsName()
        {
            lock (this)
            {
                return RegisteredMethodsName.ToArray();
            }
        }

        /// <summary>
        /// Получить список зарегистрированных методов (Forwarding)
        /// </summary>
        /// <returns></returns>
        public KeyValuePair<long, string>[] GetRegisteredForwardingName()
        {
            lock (this)
            {
                return RegisteredForwardingName.ToArray();
            }
        }
#endif

#if DEBUG
        /// <summary>
        /// Вызывается после изменения списка зарегистрированных методов
        /// </summary>
        public event Action OnChangedRegisteredMethods;
        /// <summary>
        /// Вызывается после изменения списка зарегистрированных методов (Forwarding)
        /// </summary>
        public event Action OnChangedRegisteredMethodsForwarding;
#endif
        /// <summary>
        /// Конструктор для HeadlessHandler, в обычном режиме используйте синглтон на клиенте
        /// </summary>
        public RPC()
        {
        }

        /// <summary>
        /// Возвращает информацию о зарегистрированном методе (имя + ID в DEBUG, только ID в Release)
        /// </summary>
        internal string GetMethodInfo(long ID)
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
        /// Возвращает список зарегистрированных методов для отображения
        /// </summary>
        internal string GetRegisteredMethodsList()
        {
#if DEBUG
            lock (this)
            {
                if (RegisteredMethodsName.Count == 0)
                    return "(пусто)";
                return string.Join(", ", RegisteredMethodsName.Values.Select(v => v.Item1));
            }
#else
            return "(доступно только в DEBUG)";
#endif
        }

        /// <summary>
        /// Логирует исключения для вызова RPC метода
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
        /// Попытка получить метод по ID - если не существует, вернёт null (потоко-безопасно)
        /// </summary>
        /// <param name="ID"></param>
        /// <returns></returns>
        internal MicroFunc TryGetRegisteredMethod(long ID)
        {
            lock (this)
            {
                RegisteredMethods.TryGetValue(ID, out var fun);
                return fun;
            }
        }

        /// <summary>
        /// Попытка получить метод пересылки по ID - если не существует, вернёт null (потоко-безопасно)
        /// </summary>
        /// <param name="ID"></param>
        /// <returns></returns>
        internal ForwardingInfo TryGetRegisteredForwarding(long ID)
        {
            lock (this)
            {
                RegisteredForwarding.TryGetValue(ID, out var fun);
                return fun;
            }
        }

        /// <summary>
        /// Внутренний метод регистрации метода (поддерживает множественную регистрацию)
        /// </summary>
        /// <param name="indicator"></param>
        /// <param name="micro"></param>
        /// <returns></returns>
        private IRPCRemoveHandle RegisterMethodHelp(AIndicator indicator, MicroFunc micro)
        {
            long id = indicator.ID;
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
        /// Зарегистрировать метод пересылки вызова
        /// </summary>
        /// <param name="indicator">Ссылка на метод для вызова на удалённом клиенте</param>
        /// <param name="info">Функция, возвращающая список клиентов для пересылки удалённого вызова</param>
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

        #region RegisterMethod

        /// <summary>
        /// Зарегистрировать метод для удалённого вызова RPC (без аргументов)
        /// </summary>
        /// <param name="method">метод</param>
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod(RPCfunc method, Indicator.Func indicator)
        {
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                try
                {
                    method();
                }
                catch (Exception e)
                {
                    LogRPCException(indicator, e);
                }
                return new ValueTask<IRPCReturn>((IRPCReturn)null);
            });
        }

        #endregion
        #region RegisterMethodReturned

        /// <summary>
        /// Зарегистрировать метод для удалённого вызова RPC (без аргументов, с возвращаемым значением)
        /// </summary>
        /// <param name="method">метод</param>
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<Tout>(RPCfuncOut<Tout> method, Indicator.FuncOut<Tout> indicator)
        {
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                Tout data;
                try
                {
                    data = method();
                }
                catch (Exception e)
                {
                    data = default;
                    LogRPCException(indicator, e);
                }
                @out.Set(data);
                return new ValueTask<IRPCReturn>(@out);
            });
        }

        #endregion
        #region RegisterMethodAsync (без аргументов)

        /// <summary>
        /// Зарегистрировать async-метод (Task, без аргументов) — БЕЗ блокировки потока.
        /// </summary>
        public IRPCRemoveHandle RegisterMethodAsync(RPCfuncOut<System.Threading.Tasks.Task> method, Indicator.Func indicator)
        {
            return RegisterMethodHelp(indicator, async (INGCArray array) =>
            {
                try { await method().ConfigureAwait(true); }
                catch (Exception e) { LogRPCException(indicator, e); }
                return (IRPCReturn)null;
            });
        }

        /// <summary>
        /// Зарегистрировать async-метод (Task&lt;Tout&gt;, без аргументов) — БЕЗ блокировки потока.
        /// </summary>
        public IRPCRemoveHandle RegisterMethodAsync<Tout>(RPCfuncOut<System.Threading.Tasks.Task<Tout>> method, Indicator.FuncOut<Tout> indicator)
        {
            return RegisterMethodHelp(indicator, async (INGCArray array) =>
            {
                Tout data;
                try { data = await method().ConfigureAwait(true); }
                catch (Exception e) { data = default; LogRPCException(indicator, e); }
                var @out = RPCReturn<Tout>.Create();
                @out.Set(data);
                return (IRPCReturn)@out;
            });
        }

        #endregion

        /// <summary>
        /// Позволяет удалить зарегистрированный метод
        /// </summary>
        public class RemoveHandleMethod : IRPCRemoveHandle
        {
            private readonly long ID;
            private RPC RPC;
            private MicroFunc Micro;
            private bool IsRemoved = false;

            internal RemoveHandleMethod(long id, MicroFunc micro, RPC rpc)
            {
                ID = id;
                Micro = micro;
                RPC = rpc;
            }

            /// <summary>
            /// Удаляет метод из списка зарегистрированных (его больше нельзя будет вызвать)
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
        /// Позволяет удалить зарегистрированный метод пересылки
        /// </summary>
        public class RemoveHandleForwarding : IRPCRemoveHandle
        {
            private readonly long ID;
            private RPC RPC;
            private bool IsRemoved = false;

            internal RemoveHandleForwarding(long id, RPC rpc)
            {
                ID = id;
                RPC = rpc;
            }

            /// <summary>
            /// Удаляет метод из списка зарегистрированных (его больше нельзя будет вызвать)
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
        /// Позволяет группировать несколько RemoveHandle для удаления всех сразу
        /// </summary>
        public class RemoveHandleGroup
        {
            private readonly HashSet<IRPCRemoveHandle> Handles = new HashSet<IRPCRemoveHandle>();

            /// <summary>
            /// Добавить handle в группу для удаления
            /// </summary>
            /// <param name="handle">Handle</param>
            public void Add(IRPCRemoveHandle handle)
            {
                Handles.Add(handle);
            }

            /// <summary>
            /// Удалить все зарегистрированные handles
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