using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using EMI.NetUDPv2;

[assembly: DoNotParallelize]

namespace Test.NetUDPv2
{
    /// <summary>
    /// Тесты CongestionController — системы обнаружения и реагирования на перегрузку сети.
    /// Покрывают: CWND, slow start, congestion avoidance, RTT, RTO, loss detection,
    /// fast retransmit, DeliveredRate, pacing, backoff, edge cases.
    /// </summary>
    [TestClass]
    public class Test_CongestionController
    {
        #region Initialization

        [TestMethod]
        public void Initial_CwndIsDefault()
        {
            var cc = new CongestionController();
            Assert.AreEqual(V2Constants.InitialCwnd, cc.Cwnd);
        }

        [TestMethod]
        public void Initial_SsThreshIsMax()
        {
            var cc = new CongestionController();
            Assert.AreEqual(V2Constants.MaxCwnd, cc.SsThresh);
        }

        [TestMethod]
        public void Initial_InSlowStart()
        {
            var cc = new CongestionController();
            Assert.IsTrue(cc.InSlowStart, "Should start in slow start phase");
        }

        [TestMethod]
        public void Initial_NoRttSample()
        {
            var cc = new CongestionController();
            Assert.IsFalse(cc.HasRttSample);
            Assert.AreEqual(V2Constants.InitialRtoMs, cc.Rto);
        }

        [TestMethod]
        public void Initial_DeliveredRateIsOne()
        {
            var cc = new CongestionController();
            Assert.AreEqual(1f, cc.DeliveredRate, 0.001f);
        }

        [TestMethod]
        public void Initial_InFlightIsZero()
        {
            var cc = new CongestionController();
            Assert.AreEqual(0, cc.InFlight);
            Assert.IsTrue(cc.CanSend);
        }

        [TestMethod]
        public void Initial_StatsAreZero()
        {
            var cc = new CongestionController();
            Assert.AreEqual(0L, cc.TotalAcked);
            Assert.AreEqual(0L, cc.TotalLost);
            Assert.AreEqual(0L, cc.TotalRetransmits);
        }

        #endregion

        #region Reset

        [TestMethod]
        public void Reset_RestoresDefaults()
        {
            var cc = new CongestionController();

            // Изменяем состояние
            for (int i = 0; i < 10; i++) cc.OnPacketSent();
            cc.OnPacketAcked(50);
            cc.OnPacketLost();
            cc.OnRetransmit();

            cc.Reset();

            Assert.AreEqual(V2Constants.InitialCwnd, cc.Cwnd);
            Assert.AreEqual(V2Constants.MaxCwnd, cc.SsThresh);
            Assert.AreEqual(0, cc.InFlight);
            Assert.AreEqual(0L, cc.TotalAcked);
            Assert.AreEqual(0L, cc.TotalLost);
            Assert.AreEqual(0L, cc.TotalRetransmits);
            Assert.IsFalse(cc.HasRttSample);
            Assert.AreEqual(1f, cc.DeliveredRate, 0.001f);
        }

        #endregion

        #region InFlight Tracking

        [TestMethod]
        public void OnPacketSent_IncrementsInFlight()
        {
            var cc = new CongestionController();
            cc.OnPacketSent();
            Assert.AreEqual(1, cc.InFlight);
            cc.OnPacketSent();
            Assert.AreEqual(2, cc.InFlight);
        }

        [TestMethod]
        public void OnPacketAcked_DecrementsInFlight()
        {
            var cc = new CongestionController();
            cc.OnPacketSent();
            cc.OnPacketSent();
            cc.OnPacketAcked();
            Assert.AreEqual(1, cc.InFlight);
        }

        [TestMethod]
        public void OnPacketLost_DecrementsInFlight()
        {
            var cc = new CongestionController();
            cc.OnPacketSent();
            cc.OnPacketSent();
            cc.OnPacketLost();
            Assert.AreEqual(1, cc.InFlight);
        }

        [TestMethod]
        public void InFlight_NeverGoesNegative()
        {
            var cc = new CongestionController();
            cc.OnPacketAcked();
            Assert.AreEqual(0, cc.InFlight);
            cc.OnPacketLost();
            Assert.AreEqual(0, cc.InFlight);
        }

        [TestMethod]
        public void CanSend_FalseWhenWindowFull()
        {
            var cc = new CongestionController();
            int cwnd = cc.Cwnd;

            for (int i = 0; i < cwnd; i++)
                cc.OnPacketSent();

            Assert.IsFalse(cc.CanSend, "Should not be able to send when InFlight == Cwnd");
        }

        [TestMethod]
        public void CanSend_TrueAfterAck()
        {
            var cc = new CongestionController();
            int cwnd = cc.Cwnd;

            for (int i = 0; i < cwnd; i++)
                cc.OnPacketSent();

            Assert.IsFalse(cc.CanSend);
            cc.OnPacketAcked();
            Assert.IsTrue(cc.CanSend);
        }

        #endregion

        #region Slow Start

        [TestMethod]
        public void SlowStart_CwndIncreasesOnePerAck()
        {
            var cc = new CongestionController();
            int initial = cc.Cwnd;

            cc.OnPacketSent();
            cc.OnPacketAcked();

            Assert.AreEqual(initial + 1, cc.Cwnd);
        }

        [TestMethod]
        public void SlowStart_ExponentialGrowth()
        {
            var cc = new CongestionController();
            int initial = cc.Cwnd;

            // В slow start: каждый ACK увеличивает CWND на 1
            // Если отправляем N пакетов и все подтверждаются, CWND растёт на N
            int packets = 10;
            for (int i = 0; i < packets; i++) cc.OnPacketSent();
            for (int i = 0; i < packets; i++) cc.OnPacketAcked();

            Assert.AreEqual(initial + packets, cc.Cwnd);
        }

        [TestMethod]
        public void SlowStart_CwndCappedAtMaxCwnd()
        {
            var cc = new CongestionController();

            // Отправляем и подтверждаем очень много пакетов
            for (int i = 0; i < V2Constants.MaxCwnd + 100; i++)
            {
                cc.OnPacketSent();
                cc.OnPacketAcked();
            }

            Assert.IsTrue(cc.Cwnd <= V2Constants.MaxCwnd, $"Cwnd {cc.Cwnd} exceeds max {V2Constants.MaxCwnd}");
        }

        #endregion

        #region Congestion Avoidance

        [TestMethod]
        public void CongestionAvoidance_LinearGrowth()
        {
            var cc = new CongestionController();

            // Вызываем потерю чтобы войти в congestion avoidance
            for (int i = 0; i < 20; i++) cc.OnPacketSent();
            for (int i = 0; i < 20; i++) cc.OnPacketAcked();

            // Теперь потеря — SsThresh снизится
            Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
            cc.OnPacketLost();

            int cwndAfterLoss = cc.Cwnd;
            Assert.IsFalse(cc.InSlowStart, "Should have exited slow start after loss");

            // В congestion avoidance: +1 за CWND ACK-ов
            int cwnd = cc.Cwnd;
            for (int i = 0; i < cwnd; i++)
            {
                cc.OnPacketSent();
                cc.OnPacketAcked();
            }

            // После cwnd ACK-ов cwnd должен увеличиться на 1
            Assert.AreEqual(cwndAfterLoss + 1, cc.Cwnd,
                $"Expected linear growth: {cwndAfterLoss} → {cwndAfterLoss + 1}, got {cc.Cwnd}");
        }

        [TestMethod]
        public void CongestionAvoidance_SlowerThanSlowStart()
        {
            var cc = new CongestionController();

            // Slow start: 50 ACK → +50 CWND
            for (int i = 0; i < 50; i++) { cc.OnPacketSent(); cc.OnPacketAcked(); }
            int ssGrowth = cc.Cwnd - V2Constants.InitialCwnd;

            // Loss → congestion avoidance
            Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
            cc.OnPacketLost();
            int cwndAfterLoss = cc.Cwnd;

            // Congestion avoidance: 50 ACK → ≈50/cwnd increases
            for (int i = 0; i < 50; i++) { cc.OnPacketSent(); cc.OnPacketAcked(); }
            int caGrowth = cc.Cwnd - cwndAfterLoss;

            Assert.IsTrue(caGrowth < ssGrowth,
                $"CA growth ({caGrowth}) should be slower than SS growth ({ssGrowth})");
        }

        #endregion

        #region Loss / CWND Decrease

        [TestMethod]
        public void OnPacketLost_ReducesCwnd()
        {
            var cc = new CongestionController();
            int initial = cc.Cwnd;

            Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
            cc.OnPacketLost();

            Assert.IsTrue(cc.Cwnd < initial,
                $"Cwnd should decrease on loss: was {initial}, now {cc.Cwnd}");
        }

        [TestMethod]
        public void OnPacketLost_MultiplicativeDecrease()
        {
            var cc = new CongestionController();
            int initial = cc.Cwnd;

            Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
            cc.OnPacketLost();

            int expected = (int)(initial * V2Constants.CongestionDecreaseMultiplier);
            expected = Math.Max(expected, V2Constants.MinCwnd);
            Assert.AreEqual(expected, cc.Cwnd);
        }

        [TestMethod]
        public void OnPacketLost_CwndNeverBelowMin()
        {
            var cc = new CongestionController();

            // Вызываем много потерь
            for (int i = 0; i < 50; i++)
            {
                Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
                cc.OnPacketLost();
            }

            Assert.IsTrue(cc.Cwnd >= V2Constants.MinCwnd,
                $"Cwnd {cc.Cwnd} should never go below MinCwnd {V2Constants.MinCwnd}");
        }

        [TestMethod]
        public void OnPacketLost_SetsSsThresh()
        {
            var cc = new CongestionController();

            // Grow in slow start
            for (int i = 0; i < 20; i++) { cc.OnPacketSent(); cc.OnPacketAcked(); }

            Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
            cc.OnPacketLost();

            Assert.AreEqual(cc.Cwnd, cc.SsThresh,
                "SsThresh should equal Cwnd after loss");
        }

        [TestMethod]
        public void BurstLoss_RateLimited()
        {
            var cc = new CongestionController();
            int initial = cc.Cwnd;

            // Быстрые потери подряд — только одно уменьшение CWND (защита от burst)
            cc.OnPacketLost();
            int cwndAfterFirst = cc.Cwnd;
            cc.OnPacketLost();
            cc.OnPacketLost();

            // Без задержки между потерями, CWND не должен уменьшаться повторно
            Assert.AreEqual(cwndAfterFirst, cc.Cwnd,
                "Burst losses should not cause multiple CWND decreases");
        }

        [TestMethod]
        public void SequentialLosses_DecreaseCwndMultipleTimes()
        {
            var cc = new CongestionController();
            int initial = cc.Cwnd;

            // Потери с интервалом > CongestionDecreaseIntervalMs
            Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 20);
            cc.OnPacketLost();
            int cwnd1 = cc.Cwnd;

            Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 20);
            cc.OnPacketLost();
            int cwnd2 = cc.Cwnd;

            Assert.IsTrue(cwnd2 < cwnd1 || cwnd2 == V2Constants.MinCwnd,
                $"Sequential losses should decrease CWND: {cwnd1} → {cwnd2}");
        }

        #endregion

        #region RTT / RTO

        [TestMethod]
        public void UpdateRtt_FirstSample()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(50);

            Assert.IsTrue(cc.HasRttSample);
            Assert.AreEqual(50.0, cc.SmoothedRtt, 0.001);
            Assert.AreEqual(25.0, cc.RttVariation, 0.001); // rtt/2
            Assert.AreEqual(50.0, cc.MinRtt, 0.001);
        }

        [TestMethod]
        public void UpdateRtt_SubsequentSamples_Smoothing()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(100);

            double prevSrtt = cc.SmoothedRtt;
            cc.UpdateRtt(120);

            // SRTT = (1 - 1/8) * 100 + (1/8) * 120 = 87.5 + 15 = 102.5
            Assert.AreEqual(102.5, cc.SmoothedRtt, 0.5);
        }

        [TestMethod]
        public void UpdateRtt_MinRttTracked()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(100);
            cc.UpdateRtt(50);
            cc.UpdateRtt(80);

            Assert.AreEqual(50.0, cc.MinRtt, 0.001);
        }

        [TestMethod]
        public void UpdateRtt_RtoUpdates()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(50);

            int rto = cc.Rto;
            // RTO = SRTT + 4 * RTTVAR = 50 + 4 * 25 = 150
            Assert.AreEqual(150, rto);
        }

        [TestMethod]
        public void Rto_ClampedToMinMax()
        {
            var cc = new CongestionController();

            // Очень маленький RTT
            cc.UpdateRtt(1);
            Assert.IsTrue(cc.Rto >= V2Constants.MinRtoMs,
                $"Rto {cc.Rto} should be >= MinRtoMs {V2Constants.MinRtoMs}");

            cc.Reset();

            // Очень большой RTT
            cc.UpdateRtt(5000);
            Assert.IsTrue(cc.Rto <= V2Constants.MaxRtoMs,
                $"Rto {cc.Rto} should be <= MaxRtoMs {V2Constants.MaxRtoMs}");
        }

        [TestMethod]
        public void BackoffRto_Doubles()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(50);

            int rto1 = cc.Rto;
            cc.BackoffRto();
            int rto2 = cc.Rto;

            Assert.AreEqual(Math.Min(rto1 * 2, V2Constants.MaxRtoMs), rto2);
        }

        [TestMethod]
        public void BackoffRto_CappedAtMax()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(50);

            for (int i = 0; i < 20; i++)
                cc.BackoffRto();

            Assert.AreEqual(V2Constants.MaxRtoMs, cc.Rto);
        }

        [TestMethod]
        public void UpdateRtt_IgnoresNegative()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(-5);

            Assert.IsFalse(cc.HasRttSample);
        }

        [TestMethod]
        public void Rto_StabilizesWithConsistentRtt()
        {
            var cc = new CongestionController();

            // Множество одинаковых сэмплов
            for (int i = 0; i < 100; i++)
                cc.UpdateRtt(50);

            // SRTT should converge to 50, RTTVAR should be small
            Assert.AreEqual(50.0, cc.SmoothedRtt, 1.0);
            Assert.IsTrue(cc.RttVariation < 5.0,
                $"RTT variation {cc.RttVariation} should be small with consistent samples");
        }

        [TestMethod]
        public void Rto_IncreasesWithHighJitter()
        {
            var cc = new CongestionController();

            // Стабильный RTT → RTO
            for (int i = 0; i < 50; i++)
                cc.UpdateRtt(50);
            int stableRto = cc.Rto;

            cc.Reset();

            // Нестабильный RTT (jitter)
            for (int i = 0; i < 50; i++)
                cc.UpdateRtt(i % 2 == 0 ? 20 : 100);
            int jitterRto = cc.Rto;

            Assert.IsTrue(jitterRto > stableRto,
                $"Jitter RTO ({jitterRto}) should be higher than stable RTO ({stableRto})");
        }

        #endregion

        #region OnPacketAcked with RTT

        [TestMethod]
        public void OnPacketAcked_WithRtt_UpdatesRtt()
        {
            var cc = new CongestionController();
            cc.OnPacketSent();
            cc.OnPacketAcked(75);

            Assert.IsTrue(cc.HasRttSample);
            Assert.AreEqual(75.0, cc.SmoothedRtt, 0.001);
        }

        [TestMethod]
        public void OnPacketAcked_WithNegativeRtt_SkipsRttUpdate()
        {
            var cc = new CongestionController();
            cc.OnPacketSent();
            cc.OnPacketAcked(-1);

            Assert.IsFalse(cc.HasRttSample);
        }

        #endregion

        #region DeliveredRate

        [TestMethod]
        public void DeliveredRate_DecreasesOnLoss()
        {
            var cc = new CongestionController();

            // Много потерь
            for (int i = 0; i < 100; i++)
            {
                Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 5);
                cc.OnPacketLost();
            }
            cc.ForceRecalcDeliveredRate();

            Assert.IsTrue(cc.DeliveredRate < 0.9f,
                $"DeliveredRate {cc.DeliveredRate} should decrease with heavy losses");
        }

        [TestMethod]
        public void DeliveredRate_IncreasesAfterRecovery()
        {
            var cc = new CongestionController();

            // Потери
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 5);
                cc.OnPacketLost();
            }
            cc.ForceRecalcDeliveredRate();
            float rateAfterLoss = cc.DeliveredRate;

            // Восстановление: много ACK
            for (int i = 0; i < 200; i++)
            {
                cc.OnPacketSent();
                cc.OnPacketAcked(50);
            }
            cc.ForceRecalcDeliveredRate();

            Assert.IsTrue(cc.DeliveredRate > rateAfterLoss,
                $"Rate should recover: {rateAfterLoss} → {cc.DeliveredRate}");
        }

        [TestMethod]
        public void DeliveredRate_AlwaysInRange()
        {
            var cc = new CongestionController();

            // Torture test: random acks and losses
            var rnd = new Random(42);
            for (int i = 0; i < 1000; i++)
            {
                if (rnd.NextDouble() > 0.3)
                {
                    cc.OnPacketSent();
                    cc.OnPacketAcked(rnd.Next(10, 200));
                }
                else
                {
                    Thread.Sleep(1);
                    cc.OnPacketLost();
                }

                if (i % 50 == 0)
                    cc.ForceRecalcDeliveredRate();

                Assert.IsTrue(cc.DeliveredRate >= 0f && cc.DeliveredRate <= 1f,
                    $"DeliveredRate {cc.DeliveredRate} out of range at iteration {i}");
            }
        }

        [TestMethod]
        public void DeliveredRate_PerfectDelivery_StaysHigh()
        {
            var cc = new CongestionController();

            for (int i = 0; i < 100; i++)
            {
                cc.OnPacketSent();
                cc.OnPacketAcked(50);
            }
            cc.ForceRecalcDeliveredRate();

            Assert.IsTrue(cc.DeliveredRate > 0.9f,
                $"DeliveredRate {cc.DeliveredRate} should be high with perfect delivery");
        }

        #endregion

        #region Statistics

        [TestMethod]
        public void TotalAcked_Increments()
        {
            var cc = new CongestionController();
            for (int i = 0; i < 5; i++)
            {
                cc.OnPacketSent();
                cc.OnPacketAcked();
            }
            Assert.AreEqual(5L, cc.TotalAcked);
        }

        [TestMethod]
        public void TotalLost_Increments()
        {
            var cc = new CongestionController();
            cc.OnPacketLost();
            cc.OnPacketLost();
            Assert.AreEqual(2L, cc.TotalLost);
        }

        [TestMethod]
        public void TotalRetransmits_Increments()
        {
            var cc = new CongestionController();
            cc.OnRetransmit();
            cc.OnRetransmit();
            cc.OnRetransmit();
            Assert.AreEqual(3L, cc.TotalRetransmits);
        }

        #endregion

        #region Pacing

        [TestMethod]
        public void PacingInterval_ZeroWithoutRtt()
        {
            var cc = new CongestionController();
            Assert.AreEqual(0.0, cc.PacingIntervalMs);
        }

        [TestMethod]
        public void PacingInterval_CalculatesCorrectly()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(100);

            // Pacing = RTT / CWND = 100 / InitialCwnd
            double expected = 100.0 / V2Constants.InitialCwnd;
            Assert.AreEqual(expected, cc.PacingIntervalMs, 0.01);
        }

        [TestMethod]
        public void PacingInterval_IncreasesWhenCwndDecreases()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(100);
            double pacing1 = cc.PacingIntervalMs;

            Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
            cc.OnPacketLost(); // уменьшает CWND
            double pacing2 = cc.PacingIntervalMs;

            Assert.IsTrue(pacing2 > pacing1,
                $"Pacing should increase when CWND decreases: {pacing1} → {pacing2}");
        }

        #endregion

        #region NowMs

        [TestMethod]
        public void NowMs_Advances()
        {
            var cc = new CongestionController();
            long t1 = cc.NowMs();
            Thread.Sleep(50);
            long t2 = cc.NowMs();
            Assert.IsTrue(t2 > t1, "NowMs should advance with real time");
        }

        #endregion

        #region Complex Scenarios

        [TestMethod]
        public void Scenario_NormalTraffic()
        {
            var cc = new CongestionController();

            // Симуляция нормального трафика: отправка → ACK
            for (int round = 0; round < 10; round++)
            {
                int cwnd = cc.Cwnd;
                for (int i = 0; i < cwnd; i++)
                    cc.OnPacketSent();
                for (int i = 0; i < cwnd; i++)
                    cc.OnPacketAcked(50);
            }

            Assert.IsTrue(cc.Cwnd > V2Constants.InitialCwnd,
                "CWND should grow with successful delivery");
            Assert.AreEqual(0, cc.InFlight);
            Assert.AreEqual(0L, cc.TotalLost);
        }

        [TestMethod]
        public void Scenario_CongestionAndRecovery()
        {
            var cc = new CongestionController();

            // Фаза 1: рост
            for (int i = 0; i < 100; i++)
            {
                cc.OnPacketSent();
                cc.OnPacketAcked(50);
            }
            int peakCwnd = cc.Cwnd;

            // Фаза 2: перегрузка (потери)
            Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
            cc.OnPacketLost();
            int cwndAfterLoss = cc.Cwnd;
            Assert.IsTrue(cwndAfterLoss < peakCwnd);

            // Фаза 3: восстановление
            for (int i = 0; i < 500; i++)
            {
                cc.OnPacketSent();
                cc.OnPacketAcked(50);
            }

            Assert.IsTrue(cc.Cwnd > cwndAfterLoss, "CWND should recover after loss");
        }

        [TestMethod]
        public void Scenario_HeavyPacketLoss_50Percent()
        {
            var cc = new CongestionController();
            var rnd = new Random(123);

            // Симуляция 50% потерь
            for (int i = 0; i < 200; i++)
            {
                cc.OnPacketSent();
                if (rnd.NextDouble() > 0.5)
                {
                    cc.OnPacketAcked(50 + rnd.Next(-10, 10));
                }
                else
                {
                    Thread.Sleep(1); // для CongestionDecreaseInterval
                    cc.OnPacketLost();
                }
            }

            cc.ForceRecalcDeliveredRate();

            // При 50% потерь CWND должен быть сильно ниже max
            Assert.IsTrue(cc.Cwnd < V2Constants.MaxCwnd / 2,
                $"Heavy loss should keep CWND low: {cc.Cwnd}");
            Assert.IsTrue(cc.TotalLost > 0);
        }

        [TestMethod]
        public void Scenario_PeriodicSpikes()
        {
            var cc = new CongestionController();

            for (int cycle = 0; cycle < 5; cycle++)
            {
                // Нормальный трафик
                for (int i = 0; i < 50; i++)
                {
                    cc.OnPacketSent();
                    cc.OnPacketAcked(50);
                }

                int cwndBefore = cc.Cwnd;

                // Спайк потерь (burst)
                Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
                cc.OnPacketLost();
                cc.OnPacketLost();
                cc.OnPacketLost();

                int cwndAfter = cc.Cwnd;
                // Burst-защита: CWND уменьшился только раз
                Assert.IsTrue(cwndAfter >= V2Constants.MinCwnd);
            }

            // В итоге система должна адаптироваться
            Assert.IsTrue(cc.Cwnd >= V2Constants.MinCwnd);
        }

        [TestMethod]
        public void Scenario_RttVariation()
        {
            var cc = new CongestionController();

            // RTT прыгает 30-150мс (типично для мобильных/WiFi)
            var rnd = new Random(456);
            for (int i = 0; i < 200; i++)
            {
                double rtt = 30 + rnd.NextDouble() * 120;
                cc.UpdateRtt(rtt);
            }

            Assert.IsTrue(cc.HasRttSample);
            Assert.IsTrue(cc.SmoothedRtt > 30 && cc.SmoothedRtt < 150,
                $"SRTT {cc.SmoothedRtt} should be in reasonable range with variable RTT");
            Assert.IsTrue(cc.RttVariation > 0, "RTTVAR should be positive with variable RTT");
        }

        [TestMethod]
        public void Scenario_HighBandwidth_LargeWindow()
        {
            var cc = new CongestionController();

            // Низкий RTT, отсутствие потерь → CWND должен расти до max
            for (int i = 0; i < V2Constants.MaxCwnd + 500; i++)
            {
                cc.OnPacketSent();
                cc.OnPacketAcked(5); // 5ms RTT
            }

            Assert.AreEqual(V2Constants.MaxCwnd, cc.Cwnd);
        }

        [TestMethod]
        public void Scenario_HighLatency_LowBandwidth()
        {
            var cc = new CongestionController();

            // Высокий RTT + потери
            for (int i = 0; i < 50; i++)
            {
                cc.OnPacketSent();
                cc.OnPacketAcked(500); // 500ms RTT

                if (i % 10 == 0)
                {
                    Thread.Sleep(V2Constants.CongestionDecreaseIntervalMs + 10);
                    cc.OnPacketLost();
                }
            }

            Assert.IsTrue(cc.Rto >= 500, $"RTO {cc.Rto} should reflect high RTT");
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public void ManyAcksWithoutSends_NoNegativeInFlight()
        {
            var cc = new CongestionController();
            for (int i = 0; i < 100; i++)
                cc.OnPacketAcked(50);

            Assert.AreEqual(0, cc.InFlight);
        }

        [TestMethod]
        public void ZeroRtt_HandledGracefully()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(0);

            Assert.IsTrue(cc.HasRttSample);
            Assert.IsTrue(cc.Rto >= V2Constants.MinRtoMs);
        }

        [TestMethod]
        public void VeryLargeRtt_Capped()
        {
            var cc = new CongestionController();
            cc.UpdateRtt(100000);

            Assert.AreEqual(V2Constants.MaxRtoMs, cc.Rto);
        }

        #endregion
    }
}
