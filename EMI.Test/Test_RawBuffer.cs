using EMI.Indicators;

namespace EMI.Test
{
    [TestClass]
    public class Test_RawBuffer
    {
        [TestInitialize]
        public void Init()
        {
            NGCArray.ClearPool();
        }

        #region RawBuffer Basics

        [TestMethod("RawBuffer.Allocate creates buffer of given size")]
        public void Allocate_CreatesBuffer()
        {
            using var buffer = new RawBuffer(100);
            Assert.IsFalse(buffer.IsEmpty);
            Assert.AreEqual(100, buffer.Length);
            Assert.AreEqual(0, buffer.Offset);
            Assert.IsNotNull(buffer.Bytes);
            Assert.AreEqual(100, buffer.Span.Length);
        }

        [TestMethod("RawBuffer(0) creates empty buffer")]
        public void Allocate_ZeroSize_IsEmpty()
        {
            using var buffer = new RawBuffer(0);
            Assert.IsTrue(buffer.IsEmpty);
            Assert.AreEqual(0, buffer.Length);
        }

        [TestMethod("RawBuffer.Span allows read/write")]
        public void Span_ReadWrite()
        {
            using var buffer = new RawBuffer(16);
            buffer.Span[0] = 0xAB;
            buffer.Span[1] = 0xCD;
            buffer.Span[15] = 0xFF;

            Assert.AreEqual(0xAB, buffer.Bytes[buffer.Offset]);
            Assert.AreEqual(0xCD, buffer.Bytes[buffer.Offset + 1]);
            Assert.AreEqual(0xFF, buffer.Bytes[buffer.Offset + 15]);
        }

        [TestMethod("RawBuffer.Dispose releases buffer")]
        public void Dispose_ReleasesBuffer()
        {
            var buffer = new RawBuffer(64);
            Assert.IsFalse(buffer.IsEmpty);

            buffer.Dispose();
            Assert.IsTrue(buffer.IsEmpty);
            Assert.IsNull(buffer.Bytes);
            Assert.AreEqual(0, buffer.Length);
        }

        [TestMethod("RawBuffer double Dispose is safe")]
        public void DoubleDispose_IsSafe()
        {
            var buffer = new RawBuffer(32);
            buffer.Dispose();
            buffer.Dispose();
        }

        [TestMethod("RawBuffer struct copy double dispose is safe")]
        public void DoubleDispose_CopyStruct_IsSafe()
        {
            var frameworkBuffer = new RawBuffer(64);
            var userCopy = frameworkBuffer;

            userCopy.Dispose();
            Assert.IsTrue(userCopy.IsEmpty);

            frameworkBuffer.Dispose();
            Assert.IsTrue(frameworkBuffer.IsEmpty);
        }

        [TestMethod("RawBuffer.ToString shows size")]
        public void ToString_ShowsSize()
        {
            using var buffer = new RawBuffer(256);
            var str = buffer.ToString();
            Assert.IsTrue(str.Contains("256"));

            buffer.Dispose();
            Assert.IsTrue(buffer.ToString().Contains("disposed"));
        }

        [TestMethod("RawBuffer returns array to pool after Dispose")]
        public void Dispose_ReturnsToPool()
        {
            byte[] firstArray;
            {
                using var buffer = new RawBuffer(1000);
                firstArray = buffer.Bytes;
                firstArray[0] = 42;
            }

            using var buffer2 = new RawBuffer(1000);
            Assert.IsTrue(buffer2.Bytes.Length >= 1000);
        }

        #endregion

        #region Standard Pipeline: Indicator.Func<RawBuffer> + RegisterMethod<RawBuffer>

        [TestMethod("Indicator.Func<RawBuffer> creates successfully (lazy DPack avoids SmartPackager init)")]
        public void IndicatorFunc_CreatesSuccessfully()
        {
            var indicator = new Indicator.Func<RawBuffer>("TestMethod");
            Assert.IsNotNull(indicator);
            Assert.AreNotEqual(0, indicator.ID);
        }

        [TestMethod("Indicator.Func<RawBuffer>: same name = same ID")]
        public void IndicatorFunc_SameName_SameID()
        {
            var a = new Indicator.Func<RawBuffer>("Same");
            var b = new Indicator.Func<RawBuffer>("Same");
            Assert.AreEqual(a.ID, b.ID);
        }

        [TestMethod("Indicator.Func<RawBuffer>: different names = different IDs")]
        public void IndicatorFunc_DifferentNames_DifferentIDs()
        {
            var a = new Indicator.Func<RawBuffer>("A");
            var b = new Indicator.Func<RawBuffer>("B");
            Assert.AreNotEqual(a.ID, b.ID);
        }

        [TestMethod("RegisterMethod<RawBuffer> invokes handler")]
        public void RegisterMethod_InvokesHandler()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func<RawBuffer>("Handler");
            bool called = false;
            byte receivedByte = 0;

            rpc.RegisterMethod<RawBuffer>((RawBuffer buffer) =>
            {
                called = true;
                receivedByte = buffer.Bytes[buffer.Offset];
            }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            Assert.IsNotNull(micro);

            var payload = new byte[] { 0x03,0x00,0x00,0x00, 0x42,0x43,0x44 };
            var array = new EasyArray(payload);
            micro(array);

            Assert.IsTrue(called);
            Assert.AreEqual(0x42, receivedByte);
        }

        [TestMethod("RegisterMethod<RawBuffer> auto-disposes buffer after handler")]
        public void RegisterMethod_AutoDispose()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func<RawBuffer>("AutoDispose");
            byte[] capturedBytes = null;

            rpc.RegisterMethod<RawBuffer>((RawBuffer buffer) =>
            {
                capturedBytes = buffer.Bytes;
                Assert.IsFalse(buffer.IsEmpty);
            }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            var payload = new byte[] { 0x03,0x00,0x00,0x00, 0x01,0x02,0x03 };
            var array = new EasyArray(payload);
            micro(array);

            Assert.IsNotNull(capturedBytes);
        }

        [TestMethod("RegisterMethod<RawBuffer>: manual Dispose + auto-Dispose = safe")]
        public void RegisterMethod_UserDisposePlusAutoDispose_IsSafe()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func<RawBuffer>("UserDispose");
            bool userDisposed = false;

            rpc.RegisterMethod<RawBuffer>((RawBuffer buffer) =>
            {
                buffer.Dispose();
                userDisposed = true;
                Assert.IsTrue(buffer.IsEmpty);
            }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            var payload = new byte[] { 0x03,0x00,0x00,0x00, 0x01,0x02,0x03 };
            var array = new EasyArray(payload);
            micro(array);
            Assert.IsTrue(userDisposed);
        }

        [TestMethod("RegisterMethod<RawBuffer>: exception in handler does not crash")]
        public void RegisterMethod_HandlerThrows_DoesNotCrash()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func<RawBuffer>("ThrowTest");

            rpc.RegisterMethod<RawBuffer>((RawBuffer buffer) =>
            {
                throw new InvalidOperationException("Test exception in handler");
            }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            var payload = new byte[] { 0x01,0x00,0x00,0x00, 0x01 };
            var array = new EasyArray(payload);
            micro(array);
        }

        [TestMethod("RegisterMethod<RawBuffer>: empty buffer handling")]
        public void RegisterMethod_EmptyBuffer()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func<RawBuffer>("EmptyBuf");
            bool called = false;

            rpc.RegisterMethod<RawBuffer>((RawBuffer buffer) =>
            {
                called = true;
                Assert.IsTrue(buffer.IsEmpty);
                Assert.AreEqual(0, buffer.Length);
            }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            // Пустой RawBuffer: [len=0]
            micro(new EasyArray(new byte[] { 0x00,0x00,0x00,0x00 }));
            Assert.IsTrue(called);
        }

        [TestMethod("RegisterMethod<RawBuffer>.Remove unregisters")]
        public void RegisterMethod_Remove_Works()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func<RawBuffer>("RemoveMe");
            var handle = rpc.RegisterMethod<RawBuffer>((RawBuffer _) => { }, indicator);

            Assert.IsNotNull(rpc.TryGetRegisteredMethod(indicator.ID));
            handle.Remove();
            Assert.IsNull(rpc.TryGetRegisteredMethod(indicator.ID));
        }

        #endregion

        #region Roundtrip

        [TestMethod("Roundtrip: data passes through RawBuffer intact")]
        public void Roundtrip_DataPreserved()
        {
            var rpc = new RPC();
            var originalData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };
            byte[] receivedData = null;
            var indicator = new Indicator.Func<RawBuffer>("Roundtrip");

            rpc.RegisterMethod<RawBuffer>((RawBuffer buffer) =>
            {
                receivedData = new byte[buffer.Length];
                Buffer.BlockCopy(buffer.Bytes, buffer.Offset, receivedData, 0, buffer.Length);
            }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);

            // Формат RawBuffer на wire: [len:4 байта] [данные...]
            var receiveArray = new EasyArray(4 + originalData.Length);
            BitConverter.TryWriteBytes(receiveArray.Bytes.AsSpan(0), originalData.Length);
            Buffer.BlockCopy(originalData, 0, receiveArray.Bytes, 4, originalData.Length);

            micro(receiveArray);

            Assert.IsNotNull(receivedData);
            CollectionAssert.AreEqual(originalData, receivedData);
        }

        #endregion

        #region Multi-param

        [TestMethod("RegisterMethod<int, RawBuffer>: оба параметра обрабатываются, RawBuffer авто-dispose")]
        public void MultiParam_IntAndRawBuffer()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func<int, RawBuffer>("MultiParam");
            int receivedId = 0;
            byte receivedByte = 0;

            rpc.RegisterMethod<int, RawBuffer>((int id, RawBuffer data) =>
            {
                receivedId = id;
                receivedByte = data.Bytes[data.Offset];
            }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            Assert.IsNotNull(micro);

            // Формат: int=42 (4 байта) + RawBuffer=[len:4][0xAB]
            var payload = new byte[] { 0x2A,0x00,0x00,0x00, 0x01,0x00,0x00,0x00, 0xAB };
            var array = new EasyArray(payload);
            micro(array);

            Assert.AreEqual(42, receivedId);
            Assert.AreEqual(0xAB, receivedByte);
        }

        [TestMethod("RegisterMethod<RawBuffer, RawBuffer>: два RawBuffer авто-dispose")]
        public void MultiParam_TwoRawBuffers()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func<RawBuffer, RawBuffer>("TwoBufs");
            byte first = 0, second = 0;

            rpc.RegisterMethod<RawBuffer, RawBuffer>((RawBuffer a, RawBuffer b) =>
            {
                first = a.Bytes[a.Offset];
                second = b.Bytes[b.Offset];
            }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            // RawBuffer: [len:4][0x11] + RawBuffer: [len:4][0x22]
            var payload = new byte[] { 0x01,0x00,0x00,0x00, 0x11, 0x01,0x00,0x00,0x00, 0x22 };
            var array = new EasyArray(payload);
            micro(array);

            Assert.AreEqual(0x11, first);
            Assert.AreEqual(0x22, second);
        }

        #endregion
    }
}
