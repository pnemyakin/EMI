using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EMI.NetUDPv2
{
    /// <summary>
    /// Контроллер перегрузки сети (Congestion Controller).
    /// Реализует гибрид AIMD (Additive Increase / Multiplicative Decrease) с 
    /// BBR-вдохновлённым отслеживанием bandwidth и RTT.
    /// 
    /// Потокобезопасность: ВСЕ публичные операции защищены внутренним lock.
    /// OnPacketSent вызывается из Send-потока, OnPacketAcked/OnPacketLost — из Receive/Retransmit.
    /// Без синхронизации InFlight дрейфует из-за гонок read-modify-write (InFlight++/--),
    /// что приводит к необратимому stall (InFlight >> CWND → CanSend false навсегда → speed=0).
    /// </summary>
    internal sealed class CongestionController
    {
        private readonly object _lock = new object();

        // ── Congestion Window ──────────────────────────────────────────────────

        public int Cwnd { get; private set; }
        public int SsThresh { get; private set; }
        public bool InSlowStart => Cwnd < SsThresh;

        // ── RTT ────────────────────────────────────────────────────────────────

        public double SmoothedRtt { get; private set; }
        public double RttVariation { get; private set; }
        public double MinRtt { get; private set; }
        public int Rto { get; private set; }
        public bool HasRttSample { get; private set; }

        // ── Delivery Rate ──────────────────────────────────────────────────────

        public float DeliveredRate { get; private set; }

        // ── Flight tracking ────────────────────────────────────────────────────

        public int InFlight { get; private set; }

        /// <summary>
        /// Можно ли отправить ещё один пакет. Атомарная проверка InFlight и Cwnd.
        /// </summary>
        public bool CanSend
        {
            get { lock (_lock) { return InFlight < Cwnd; } }
        }

        // ── Statistics ─────────────────────────────────────────────────────────

        public long TotalAcked { get; private set; }
        public long TotalLost { get; private set; }
        public long TotalRetransmits { get; private set; }

        // ── Internal ───────────────────────────────────────────────────────────

        private long _lastDecreaseTimestamp;
        private int _ackedSinceLastIncrease;
        private long _deliveredWindow;
        private long _lostWindow;
        private long _lastRateCalcTimestamp;
        private readonly Stopwatch _sw;

        // ── Constructor ────────────────────────────────────────────────────────

        public CongestionController()
        {
            _sw = Stopwatch.StartNew();
            Reset();
        }

        public void Reset()
        {
            lock (_lock)
            {
                Cwnd = V2Constants.InitialCwnd;
                SsThresh = V2Constants.MaxCwnd;
                SmoothedRtt = 0;
                RttVariation = 0;
                MinRtt = double.MaxValue;
                Rto = V2Constants.InitialRtoMs;
                HasRttSample = false;
                DeliveredRate = 1f;
                InFlight = 0;
                TotalAcked = 0;
                TotalLost = 0;
                TotalRetransmits = 0;
                _lastDecreaseTimestamp = 0;
                _ackedSinceLastIncrease = 0;
                _deliveredWindow = 0;
                _lostWindow = 0;
                _lastRateCalcTimestamp = _sw.ElapsedMilliseconds;
            }
        }

        // ── Packet Lifecycle ───────────────────────────────────────────────────

        public void OnPacketSent()
        {
            lock (_lock) { InFlight++; }
        }

        /// <summary>
        /// Синхронизирует InFlight с реальным количеством pending пакетов.
        /// Вызывается из тик-лупа под _pendingLock для устранения дрейфа.
        /// </summary>
        public void SyncInFlight(int pendingCount)
        {
            lock (_lock) { InFlight = pendingCount; }
        }

        public void OnPacketAcked(double rttMs = -1)
        {
            lock (_lock)
            {
                InFlight = Math.Max(0, InFlight - 1);
                TotalAcked++;
                _deliveredWindow++;

                if (rttMs >= 0)
                    UpdateRttLocked(rttMs);

                if (InSlowStart)
                {
                    Cwnd = Math.Min(Cwnd + 1, V2Constants.MaxCwnd);
                }
                else
                {
                    _ackedSinceLastIncrease++;
                    if (_ackedSinceLastIncrease >= Cwnd)
                    {
                        _ackedSinceLastIncrease = 0;
                        int increase = Math.Max((int)V2Constants.CongestionIncreasePerRtt, Cwnd >> 3);
                        Cwnd = Math.Min(Cwnd + increase, V2Constants.MaxCwnd);
                    }
                }

                RecalcDeliveredRateLocked();
            }
        }

        public void OnPacketLost()
        {
            lock (_lock)
            {
                TotalLost++;
                _lostWindow++;

                long now = _sw.ElapsedMilliseconds;

                int decreaseInterval = Math.Max(V2Constants.CongestionDecreaseIntervalMs, (int)SmoothedRtt);
                if (now - _lastDecreaseTimestamp >= decreaseInterval)
                {
                    _lastDecreaseTimestamp = now;

                    // SsThresh = pre-loss CWND, но не ниже InitialCwnd.
                    // Гарантируем: после снижения Cwnd < SsThresh → InSlowStart = true
                    // → экспоненциальный рост быстро восстанавливает окно.
                    // Минимум InitialCwnd (64): при Cwnd=MinCwnd=32 без этого
                    // SsThresh=32=Cwnd → InSlowStart=false → CA mode (+4/RTT) →
                    // пропускная способность 1 пакет/RTT ≈ 0.14 Мбит навсегда.
                    int preLossCwnd = Cwnd;
                    SsThresh = Math.Max(Cwnd, V2Constants.InitialCwnd);

                    int newCwnd = (int)(Cwnd * V2Constants.CongestionDecreaseMultiplier);
                    Cwnd = Math.Max(newCwnd, V2Constants.MinCwnd);
                    _ackedSinceLastIncrease = 0;


                }

                // InFlight НЕ зажимаем: SyncInFlight() в тик-лупе держит InFlight
                // точно равным _pendingReliable.Count. Зажатие InFlight < pending
                // врёт о реальном количестве неподтверждённых пакетов → burst →
                // ещё больше потерь. Если InFlight > Cwnd — значит столько пакетов
                // реально ждут ACK, и отправлять новые нельзя (CanSend=false).
                // Выход из stall — только через ACK или disconnect по таймауту.

                RecalcDeliveredRateLocked();
            }
        }

        public void OnRetransmit()
        {
            lock (_lock) { TotalRetransmits++; }
        }

        // ── RTT ────────────────────────────────────────────────────────────────

        public void UpdateRtt(double rttMs)
        {
            lock (_lock) { UpdateRttLocked(rttMs); }
        }

        private void UpdateRttLocked(double rttMs)
        {
            if (rttMs < 0) return;

            if (rttMs < MinRtt)
                MinRtt = rttMs;

            if (!HasRttSample)
            {
                HasRttSample = true;
                SmoothedRtt = rttMs;
                RttVariation = rttMs / 2.0;
            }
            else
            {
                double diff = Math.Abs(SmoothedRtt - rttMs);
                RttVariation = (1.0 - 0.25) * RttVariation + 0.25 * diff;
                SmoothedRtt = (1.0 - 0.125) * SmoothedRtt + 0.125 * rttMs;
            }

            int rto = (int)(SmoothedRtt + Math.Max(1.0, 4.0 * RttVariation));
            Rto = Math.Clamp(rto, V2Constants.MinRtoMs, V2Constants.MaxRtoMs);
        }

        public void BackoffRto()
        {
            lock (_lock) { Rto = Math.Min(Rto * 2, V2Constants.MaxRtoMs); }
        }

        // ── Delivered Rate ─────────────────────────────────────────────────────

        private void RecalcDeliveredRateLocked()
        {
            long now = _sw.ElapsedMilliseconds;

            if (now - _lastRateCalcTimestamp < 200)
                return;

            _lastRateCalcTimestamp = now;

            long total = _deliveredWindow + _lostWindow;
            if (total == 0)
            {
                DeliveredRate = 1f;
            }
            else
            {
                float rate = (float)_deliveredWindow / total;
                DeliveredRate = DeliveredRate * 0.7f + rate * 0.3f;
                DeliveredRate = Math.Clamp(DeliveredRate, 0f, 1f);
            }

            _deliveredWindow = _deliveredWindow * 3 / 4;
            _lostWindow = _lostWindow * 3 / 4;
        }

        public void ForceRecalcDeliveredRate()
        {
            lock (_lock)
            {
                _lastRateCalcTimestamp = 0;
                RecalcDeliveredRateLocked();
            }
        }

        // ── Pacing ─────────────────────────────────────────────────────────────

        public double PacingIntervalMs
        {
            get
            {
                if (!HasRttSample || Cwnd <= 0)
                    return 0;
                return SmoothedRtt / Cwnd;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long NowMs() => _sw.ElapsedMilliseconds;
    }
}
