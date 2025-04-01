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
    /// Отвечает за регистрирование удалённых процедур для последующего вызова
    /// </summary>
    public class RPC
    {
        /// <summary>
        /// Содержит в себе код по запуску функции
        /// </summary>
        /// <param name="array">необработанный пакет данных</param>
        /// <returns></returns>
        internal delegate IRPCReturn MicroFunc(INGCArray array);
        /// <summary>
        /// Указывает каким клиентам необходимо выполнить пересылку
        /// </summary>
        /// <param name="sendClient">какой клиент хочет произвевти пересылку</param>
        /// <returns></returns>
        public delegate Client[] ForwardingInfo(Client sendClient);
        /// <summary>
        /// Зарегестрированные функции - используется при вызове
        /// </summary>
        private readonly Dictionary<int, MicroFunc> RegisteredMethods = new Dictionary<int, MicroFunc>();
        private readonly Dictionary<int, ForwardingInfo> RegisteredForwarding = new Dictionary<int, ForwardingInfo>();

#if DEBUG
        private readonly Dictionary<int, (string, int)> RegisteredMethodsName = new Dictionary<int, (string, int)>();
        private readonly Dictionary<int,string> RegisteredForwardingName = new Dictionary<int, string>();
#endif

#if DEBUG
        /// <summary>
        /// Получает список зарегистрированных функций
        /// </summary>
        /// <returns></returns>
        public KeyValuePair<int,(string,int)>[] GetRegisteredMethodsName()
        {
            return RegisteredMethodsName.ToArray();
        }

        /// <summary>
        /// Получает список зарегистрированных функций (Forwarding)
        /// </summary>
        /// <returns></returns>
        public KeyValuePair<int, string>[] GetRegisteredForwardingName()
        {
            return RegisteredForwardingName.ToArray();
        }
#endif

#if DEBUG
        /// <summary>
        /// Вызывается когда изменён список зарегистрированных методов
        /// </summary>
        public event Action OnChangedRegisteredMethods;
        /// <summary>
        /// Вызывается когда изменён список зарегистрированных методов (Forwarding)
        /// </summary>
        public event Action OnChangedRegisteredMethodsForwarding;
#endif
        /// <summary>
        /// Необходим для HeadlessHandler, в остальных случаях конструктор вызывать не требуется
        /// </summary>
        public RPC()
        {
        }

        /// <summary>
        /// Пытается получить функцию по айди - если не получиться вернёт null (потоко-безопасен)
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
        /// Пытается получить функцию по айди - если не получиться вернёт null (потоко-безопасен)
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
        /// Производит основную регистрацию метода (остаётся только написать микрофункцию)
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
        /// Регестрирует метод пересылки вызовов
        /// </summary>
        /// <param name="indicator">ссылка на функцию для вызова на указанных клиентах</param>
        /// <param name="info">функция будет вызываться при пересылке сообщений и должна вернуть список клиентов которым необходимо отправить пересылку</param>
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
        /// Регистрирует метод для возможности вызвать его
        /// </summary>
        /// <param name="method">метод</param>
        /// <param name="indicator">имя ключа</param>
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
                     Console.WriteLine("EMI RPC => " + e);
                 }
                 return null;
             });
        }

        /// <summary>
        /// Регистрирует метод для возможности вызвать его
        /// </summary>
        /// <param name="method">метод</param>
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1>(RPCfunc<T1> method, Indicator.Func<T1> indicator)
        {
            var packager = Packager.Create<T1>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
             {
                 packager.UnPack(array.Bytes, array.Offset, out T1 t1);
                 try
                 {
                     method(t1);
                 }
                 catch (Exception e)
                 {
                     //TODO Loging
                     Console.WriteLine("EMI RPC => " + e);
                 }
                 return null;
             });
        }

        /// <summary>
        /// Регистрирует метод для возможности вызвать его
        /// </summary>
        /// <param name="method">метод</param>
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1, T2>(RPCfunc<T1, T2> method, Indicator.Func<T1, T2> indicator)
        {
            var packager = Packager.Create<T1, T2>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2);
                try
                {
                    method(t1, t2);
                }
                catch (Exception e)
                {
                    Console.WriteLine("EMI RPC => " + e);
                }
                return null;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1, T2, T3>(RPCfunc<T1, T2, T3> method, Indicator.Func<T1, T2, T3> indicator)
        {
            var packager = Packager.Create<T1, T2, T3>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3);
                try
                {
                    method(t1, t2, t3);
                }
                catch (Exception e)
                {
                    Console.WriteLine("EMI RPC => " + e);
                }
                return null;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1, T2, T3, T4>(RPCfunc<T1, T2, T3, T4> method, Indicator.Func<T1, T2, T3, T4> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4);
                try
                {
                    method(t1, t2, t3, t4);
                }
                catch (Exception e)
                {
                    Console.WriteLine("EMI RPC => " + e);
                }
                return null;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1, T2, T3, T4, T5>(RPCfunc<T1, T2, T3, T4, T5> method, Indicator.Func<T1, T2, T3, T4, T5> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5);
                try
                {
                    method(t1, t2, t3, t4, t5);
                }
                catch (Exception e)
                {
                    Console.WriteLine("EMI RPC => " + e);
                }
                return null;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1, T2, T3, T4, T5, T6>(RPCfunc<T1, T2, T3, T4, T5, T6> method, Indicator.Func<T1, T2, T3, T4, T5, T6> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6);
                try
                {
                    method(t1, t2, t3, t4, t5, t6);
                }
                catch (Exception e)
                {
                    Console.WriteLine("EMI RPC => " + e);
                }
                return null;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1, T2, T3, T4, T5, T6, T7>(RPCfunc<T1, T2, T3, T4, T5, T6, T7> method, Indicator.Func<T1, T2, T3, T4, T5, T6, T7> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6, T7>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6, out T7 t7);
                try
                {
                    method(t1, t2, t3, t4, t5, t6, t7);
                }
                catch (Exception e)
                {
                    Console.WriteLine("EMI RPC => " + e);
                }
                return null;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1, T2, T3, T4, T5, T6, T7, T8>(RPCfunc<T1, T2, T3, T4, T5, T6, T7, T8> method, Indicator.Func<T1, T2, T3, T4, T5, T6, T7, T8> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6, out T7 t7, out T8 t8);
                try
                {
                    method(t1, t2, t3, t4, t5, t6, t7, t8);
                }
                catch (Exception e)
                {
                    Console.WriteLine("EMI RPC => " + e);
                }
                return null;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1, T2, T3, T4, T5, T6, T7, T8, T9>(RPCfunc<T1, T2, T3, T4, T5, T6, T7, T8, T9> method, Indicator.Func<T1, T2, T3, T4, T5, T6, T7, T8, T9> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6, out T7 t7, out T8 t8, out T9 t9);
                try
                {
                    method(t1, t2, t3, t4, t5, t6, t7, t8, t9);
                }
                catch (Exception e)
                {
                    Console.WriteLine("EMI RPC => " + e);
                }
                return null;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(RPCfunc<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> method, Indicator.Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6, out T7 t7, out T8 t8, out T9 t9, out T10 t10);
                try
                {
                    method(t1, t2, t3, t4, t5, t6, t7, t8, t9, t10);
                }
                catch (Exception
        e)
                {
                    Console.WriteLine("EMI RPC => " + e);
                }
                return null;
            });
        }
        #endregion
        #region RegisterMethodReturned
        /// <summary>
        /// Регистрирует метод для возможности вызвать его
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
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary>
        /// Регистрирует метод для возможности вызвать его
        /// </summary>
        /// <param name="method">метод</param>
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<Tout, T1>(RPCfuncOut<Tout, T1> method, Indicator.FuncOut<Tout,T1> indicator)
        {
            var packager = Packager.Create<T1>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1);
                Tout data;
                try
                {
                    data = method(t1);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary>
        /// Регистрирует метод для возможности вызвать его
        /// </summary>
        /// <param name="method">метод</param>
        /// <param name="indicator">ссылка на метод</param>
        public IRPCRemoveHandle RegisterMethod<Tout, T1, T2>(RPCfuncOut<Tout, T1, T2> method, Indicator.FuncOut<Tout, T1, T2> indicator)
        {
            var packager = Packager.Create<T1,T2>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1,out T2 t2);
                Tout data;
                try
                {
                    data = method(t1, t2);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param> 
        public IRPCRemoveHandle RegisterMethod<Tout, T1, T2, T3>(RPCfuncOut<Tout, T1, T2, T3> method, Indicator.FuncOut<Tout, T1, T2, T3> indicator)
        {
            var packager = Packager.Create<T1, T2, T3>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3);
                Tout data;
                try
                {
                    data = method(t1, t2, t3);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param> 
        public IRPCRemoveHandle RegisterMethod<Tout, T1, T2, T3, T4>(RPCfuncOut<Tout, T1, T2, T3, T4> method, Indicator.FuncOut<Tout, T1, T2, T3, T4> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4);
                Tout data;
                try
                {
                    data = method(t1, t2, t3, t4);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param> 
        public IRPCRemoveHandle RegisterMethod<Tout, T1, T2, T3, T4, T5>(RPCfuncOut<Tout, T1, T2, T3, T4, T5> method, Indicator.FuncOut<Tout, T1, T2, T3, T4, T5> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5);
                Tout data;
                try
                {
                    data = method(t1, t2, t3, t4, t5);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param> 
        public IRPCRemoveHandle RegisterMethod<Tout, T1, T2, T3, T4, T5, T6>(RPCfuncOut<Tout, T1, T2, T3, T4, T5, T6> method, Indicator.FuncOut<Tout, T1, T2, T3, T4, T5, T6> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6);
                Tout data;
                try
                {
                    data = method(t1, t2, t3, t4, t5, t6);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param> 
        public IRPCRemoveHandle RegisterMethod<Tout, T1, T2, T3, T4, T5, T6, T7>(RPCfuncOut<Tout, T1, T2, T3, T4, T5, T6, T7> method, Indicator.FuncOut<Tout, T1, T2, T3, T4, T5, T6, T7> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6, T7>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6, out T7 t7);
                Tout data;
                try
                {
                    data = method(t1, t2, t3, t4, t5, t6, t7);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param> 
        public IRPCRemoveHandle RegisterMethod<Tout, T1, T2, T3, T4, T5, T6, T7, T8>(RPCfuncOut<Tout, T1, T2, T3, T4, T5, T6, T7, T8> method, Indicator.FuncOut<Tout, T1, T2, T3, T4, T5, T6, T7, T8> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6, out T7 t7, out T8 t8);
                Tout data;
                try
                {
                    data = method(t1, t2, t3, t4, t5, t6, t7, t8);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param> 
        public IRPCRemoveHandle RegisterMethod<Tout, T1, T2, T3, T4, T5, T6, T7, T8, T9>(RPCfuncOut<Tout, T1, T2, T3, T4, T5, T6, T7, T8, T9> method, Indicator.FuncOut<Tout, T1, T2, T3, T4, T5, T6, T7, T8, T9> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6, out T7 t7, out T8 t8, out T9 t9);
                Tout data;
                try
                {
                    data = method(t1, t2, t3, t4, t5, t6, t7, t8, t9);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        /// <summary> 
        /// Регистрирует метод для возможности вызвать его 
        /// </summary> 
        /// <param name="method">метод</param> 
        /// <param name="indicator">ссылка на метод</param> 
        public IRPCRemoveHandle RegisterMethod<Tout, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(RPCfuncOut<Tout, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> method, Indicator.FuncOut<Tout, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> indicator)
        {
            var packager = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>();
            var @out = RPCReturn<Tout>.Create();
            return RegisterMethodHelp(indicator, (INGCArray array) =>
            {
                packager.UnPack(array.Bytes, array.Offset, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5, out T6 t6, out T7 t7, out T8 t8, out T9 t9, out T10 t10);
                Tout data;
                try
                {
                    data = method(t1, t2, t3, t4, t5, t6, t7, t8, t9, t10);
                }
                catch (Exception e)
                {
                    data = default;
                    Console.WriteLine("EMI RPC => " + e);
                }
                @out.Set(data);
                return @out;
            });
        }

        #endregion

        /// <summary>
        /// Позволяет удалить зарегистрированный метод
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
            /// Удаляет метод из списка зарегестрированных (его больше нельзя будет вызвать)
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
        /// Позволяет удалить зарегистрированный метод
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
            /// Удаляет метод из списка зарегестрированных (его больше нельзя бужет вызвать)
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
        /// Позволяет добавить все методы в группу что бы удалить всю группу когда это будет нужно
        /// </summary>
        public class RemoveHandleGroup
        {
            private readonly HashSet<IRPCRemoveHandle> Handles = new HashSet<IRPCRemoveHandle>();

            /// <summary>
            /// Добавить метод в группу для удаления
            /// </summary>
            /// <param name="handle">метод</param>
            public void Add(IRPCRemoveHandle handle)
            {
                Handles.Add(handle);
            }

            /// <summary>
            /// Удалить все добавленные методы
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