using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EMI.DebugLog
{
    using Headless;
    /// <summary>
    /// Для вывода отладочной информации
    /// </summary>
    public class Logger
    {
        /// <summary>
        /// Вызывается когда EMI логирует сообщение
        /// </summary>
        /// <param name="client">клиент который вызвал сообщение или null если сервер</param>
        /// <param name="type">тип сообщения</param>
        /// <param name="time">время когда было сгенерировано сообщение</param>
        /// <param name="message">текст сообщения</param>
        public delegate void Message(Client client, LogType type, DateTime time, string message);
#if DEBUG
        /// <summary>
        /// Вызывается при создании сообщения
        /// </summary>
        public event Message OnMessage;
#endif
        internal Logger()
        {

        }

//        private static string Trace()
//        {
//#if DEBUG
//            return $"\nDebugTrace: {DebugUtil.GetStackTrace()}";
//#else
//            return "";
//#endif
//        }

        internal void Log(LogMessage message,params object[] format)
        {
#if DEBUG
            string msg = string.Format(message.Message, format);
            Console.WriteLine($"EMI => {message.Type} => {msg}");
            OnMessage?.Invoke(null, message.Type, DateTime.Now, msg);
#endif
        }

        internal void Log(Client client, LogMessage message, params object[] format)
        {
#if DEBUG
            string msg = string.Format(message.Message, format);
            Console.WriteLine($"EMI => {message.Type} => client: {client.RemoteAddress} => {msg}");
            OnMessage?.Invoke(client, message.Type, DateTime.Now, msg);
#endif
        }


        internal void Log(HeadlessHandler client, LogMessage message, params object[] format)
        {
#if DEBUG
            string msg = string.Format(message.Message, format);
            Console.WriteLine($"EMI => {message.Type} => client: {client.RemoteAddress} => {msg}");
            OnMessage?.Invoke(null, message.Type, DateTime.Now, msg);
#endif
        }
    }
}
