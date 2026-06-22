using System.Threading;

namespace EMI
{
    using NGC;
    using Indicators;

    internal class RCWaitHandle
    {
        public SemaphoreSlim Semaphore = new SemaphoreSlim(0, 1);
        public AIndicator Indicator;
        /// <summary>
        /// Уникальный идентификатор вызова для сопоставления запроса и ответа.
        /// </summary>
        public int CallID;
        /// <summary>
        /// Сырые данные ответа для распаковки вызывающей стороной.
        /// Заполняется при получении RPC_Returned.
        /// </summary>
        public INGCArray ResultData;

        public RCWaitHandle(AIndicator indicator, int callId)
        {
            Indicator = indicator;
            CallID = callId;
        }
    }
}