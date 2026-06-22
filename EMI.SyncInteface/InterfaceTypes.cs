using System;
using System.Collections.Generic;
using System.Threading;

namespace EMI.SyncInterface
{
#if DEBUG
    /// <summary>
    /// Хранит предупреждения валидации интерфейса с потокобезопасным флагом логирования.
    /// Используется reference type для корректной работы с ConcurrentDictionary.
    /// </summary>
    internal class ValidationState
    {
        public readonly List<string> Warnings = new List<string>();
        private int _logged = 0;

        /// <summary>
        /// Пытается пометить предупреждения как залогированные.
        /// Возвращает true только для первого вызова (атомарно).
        /// </summary>
        public bool TryMarkLogged() => Interlocked.Exchange(ref _logged, 1) == 0;
    }
#endif

    internal struct InterfaceTypes
    {
        public Type Client;
        public List<FieldList> ClientFields;
        public List<MarkeredMethod> ClientMethods;
        public Type Server;
        public List<FieldList> ServerFields;
        public List<MarkeredMethod> ServerMethods;
#if DEBUG
        /// <summary>
        /// Предупреждения валидации, собранные при построении интерфейса.
        /// Логируются при первом вызове RegisterClass.
        /// </summary>
        public ValidationState Validation;
#endif

        public InterfaceTypes(Type client, List<FieldList> cf, List<MarkeredMethod> cm, Type server, List<FieldList> sf, List<MarkeredMethod> sm)
        {
            Client = client;
            ClientFields = cf;
            ClientMethods = cm;
            Server = server;
            ServerFields = sf;
            ServerMethods = sm;
#if DEBUG
            Validation = new ValidationState();
#endif
        }
    }
}