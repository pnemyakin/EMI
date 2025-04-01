using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EMI
{
    using NGC;

    /// <summary>
    /// База клиента EMI, была создана, так как имеется несколько классов/типов клиентов
    /// </summary>
    public abstract class AClient
    {
        internal Dictionary<int, RCWaitHandle> RPCReturn { get; set; }
        /// <summary>
        /// Массив запросов на ожидание ответа (возврат значения)
        /// </summary>
        internal abstract Task Send(INGCArray array, bool guaranteed, CancellationToken token);
    }
}