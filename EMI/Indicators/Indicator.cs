using System.Threading;
using System.Threading.Tasks;
using SmartPackager;

namespace EMI.Indicators
{
    using NGC;
    /// <summary>
    /// Позволяет создать ссылку на удалённый метод
    /// </summary>
    public static class Indicator
    {
        /// <summary>
        /// Ссылка на удалённый метод
        /// </summary>
        public sealed class Func : AIndicator
        {
            /// <summary>
            /// Позволяет создать ссылку на удалённый метод
            /// </summary>
            /// <param name="name">имя ключа</param>
            /// <returns></returns>
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            private Func()
            {

            }

            /// <summary>
            /// Вызвать удалённый метод на указанном клиенте
            /// </summary>
            /// <param name="client">клиент на котором будет вызван метод</param>
            /// <param name="token">токен отмены операции</param>
            /// <param name="type">тип вызова</param>
            /// <returns></returns>
            public async Task RCall(AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

#pragma warning disable CS1591 // Отсутствует комментарий XML для открытого видимого типа или члена
            protected internal override int Size => 0;

            protected internal override void PackUp(INGCArray array)
            {

            }

            protected internal override void UnPack(INGCArray array)
            {

            }
#pragma warning restore CS1591 // Отсутствует комментарий XML для открытого видимого типа или члена
        }

        /// <summary>
        /// Ссылка на удалённый метод
        /// </summary>
        public sealed class Func<T1> : AIndicator
        {
            /// <summary>
            /// Позволяет создать ссылку на удалённый метод
            /// </summary>
            /// <param name="name">имя ключа</param>
            /// <returns></returns>
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            /// <summary>
            /// Вызвать удалённый метод на указанном клиенте
            /// </summary>
            /// <param name="t1">параметр функции</param>
            /// <param name="client">клиент на котором будет вызван метод</param>
            /// <param name="token">токен отмены операции</param>
            /// <param name="type">тип вызова</param>
            /// <returns></returns>
            public async Task RCall(T1 t1, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            /// <summary>
            /// Размер необходимый для упаковки параметров
            /// </summary>
            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1);

            private readonly static Packager.M<T1> DPackUp = Packager.Create<T1>();
            private T1 Dt1;

            /// <summary>
            /// Упаковщик
            /// </summary>
            /// <param name="array"></param>
            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1);
            }

            /// <summary>
            /// Распаковщик
            /// </summary>
            /// <param name="array"></param>
            protected internal override void UnPack(INGCArray array)
            {

            }
        }

        /// <summary>
        /// Ссылка на удалённый метод
        /// </summary>
        public sealed class Func<T1, T2> : AIndicator
        {
            /// <summary>
            /// Позволяет создать ссылку на удалённый метод
            /// </summary>
            /// <param name="name">имя ключа</param>
            /// <returns></returns>
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            /// <summary>
            /// Вызвать удалённый метод на указанном клиенте
            /// </summary>
            /// <param name="t1">параметр #1 функции</param>
            /// <param name="t2">параметр #2 функции</param>
            /// <param name="client">клиент на котором будет вызван метод</param>
            /// <param name="token">токен отмены операции</param>
            /// <param name="type">тип вызова</param>
            /// <returns></returns>
            public async Task RCall(T1 t1, T2 t2, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            /// <summary>
            /// Размер необходимый для упаковки параметров
            /// </summary>
            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1, Dt2);

            private readonly static Packager.M<T1, T2> DPackUp = Packager.Create<T1, T2>();
            private T1 Dt1;
            private T2 Dt2;

            /// <summary>
            /// Упаковщик
            /// </summary>
            /// <param name="array"></param>
            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2);
            }

            /// <summary>
            /// Распаковщик
            /// </summary>
            /// <param name="array"></param>
            protected internal override void UnPack(INGCArray array)
            {

            }
        }
#pragma warning disable CS1591 // (я заебусь это коментировать...) (сгенерировано ChatGPT-3.5) Отсутствует комментарий XML для открытого видимого типа или члена
        public sealed class Func<T1, T2, T3> : AIndicator
        {
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task RCall(T1 t1, T2 t2, T3 t3, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1, Dt2, Dt3);

            private readonly static Packager.M<T1, T2, T3> DPackUp = Packager.Create<T1, T2, T3>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3);
            }

            protected internal override void UnPack(INGCArray array)
            {

            }
        }

        public sealed class Func<T1, T2, T3, T4> : AIndicator
        {
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
            Name = name;
#endif
            }

            public async Task RCall(T1 t1, T2 t2, T3 t3, T4 t4, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4);

            private readonly static Packager.M<T1, T2, T3, T4> DPackUp = Packager.Create<T1, T2, T3, T4>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4);
            }

            protected internal override void UnPack(INGCArray array)
            {

            }
        }

        public sealed class Func<T1, T2, T3, T4, T5> : AIndicator
        {
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5);

            private readonly static Packager.M<T1, T2, T3, T4, T5> DPackUp = Packager.Create<T1, T2, T3, T4, T5>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5);
            }

            protected internal override void UnPack(INGCArray array)
            {

            }
        }

        public sealed class Func<T1, T2, T3, T4, T5, T6> : AIndicator
        {
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6);

            private readonly static Packager.M<T1, T2, T3, T4, T5, T6> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6);
            }

            protected internal override void UnPack(INGCArray array)
            {

            }
        }

        public sealed class Func<T1, T2, T3, T4, T5, T6, T7> : AIndicator
        {
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                Dt7 = t7;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7);

            private readonly static Packager.M<T1, T2, T3, T4, T5, T6, T7> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6, T7>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;
            private T7 Dt7;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7);
            }

            protected internal override void UnPack(INGCArray array)
            {

            }
        }

        public sealed class Func<T1, T2, T3, T4, T5, T6, T7, T8> : AIndicator
        {
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                Dt7 = t7;
                Dt8 = t8;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8);

            private readonly static Packager.M<T1, T2, T3, T4, T5, T6, T7, T8> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;
            private T7 Dt7;
            private T8 Dt8;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8);
            }

            protected internal override void UnPack(INGCArray array)
            {

            }
        }

        public sealed class Func<T1, T2, T3, T4, T5, T6, T7, T8, T9> : AIndicator
        {
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, T9 t9, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                Dt7 = t7;
                Dt8 = t8;
                Dt9 = t9;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8, Dt9);

            private readonly static Packager.M<T1, T2, T3, T4, T5, T6, T7, T8, T9> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;
            private T7 Dt7;
            private T8 Dt8;
            private T9 Dt9;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8, Dt9);
            }

            protected internal override void UnPack(INGCArray array)
            {

            }
        }

        public sealed class Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : AIndicator
        {
            public Func(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, T9 t9, T10 t10, AClient client, RCType type = RCType.Guaranteed, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                Dt7 = t7;
                Dt8 = t8;
                Dt9 = t9;
                Dt10 = t10;
                await RCallLow(client, type, token).ConfigureAwait(false);
            }

            protected internal override int Size => (int)DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8, Dt9, Dt10);

            private readonly static Packager.M<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;
            private T7 Dt7;
            private T8 Dt8;
            private T9 Dt9;
            private T10 Dt10;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8, Dt9, Dt10);
            }

            protected internal override void UnPack(INGCArray array)
            {

            }
        }
#pragma warning restore CS1591 // Отсутствует комментарий XML для открытого видимого типа или члена
        /// <summary>
        /// Ссылка на удалённый метод
        /// </summary>
        public sealed class FuncOut<TOut> : AIndicator
        {
            /// <summary>
            /// Позволяет создать ссылку на удалённый метод
            /// </summary>
            /// <param name="name">имя ключа</param>
            /// <returns></returns>
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            /// <summary>
            /// Вызвать удалённый метод на указанном клиенте
            /// </summary>
            /// <param name="client">клиент на котором будет вызван метод</param>
            /// <param name="token">токен отмены операции</param>
            /// <param name="type">тип вызова</param>
            /// <returns></returns>
            public async Task<TOut> RCall(AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

#pragma warning disable CS1591 // Отсутствует комментарий XML для открытого видимого типа или члена
            protected internal override int Size => 0;

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;

            protected internal override void PackUp(INGCArray array)
            {
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
#pragma warning restore CS1591 // Отсутствует комментарий XML для открытого видимого типа или члена
        }

        /// <summary>
        /// Ссылка на удалённый метод
        /// </summary>
        public sealed class FuncOut<TOut, T1> : AIndicator
        {
            /// <summary>
            /// Позволяет создать ссылку на удалённый метод
            /// </summary>
            /// <param name="name">имя ключа</param>
            /// <returns></returns>
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            /// <summary>
            /// Вызвать удалённый метод на указанном клиенте
            /// </summary>
            /// <param name="t1">параметр функции</param>
            /// <param name="client">клиент на котором будет вызван метод</param>
            /// <param name="token">токен отмены операции</param>
            /// <param name="type">тип вызова</param>
            /// <returns></returns>
            public async Task<TOut> RCall(T1 t1, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }
            /// <summary>
            /// Расчитывает размер необходимый для упаковки
            /// </summary>
            protected internal override int Size => DPackUp.CalcNeedSize(Dt1);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1> DPackUp = Packager.Create<T1>();
            private T1 Dt1;

            /// <summary>
            /// Упаковка
            /// </summary>
            /// <param name="array">массив для упаковки</param>
            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1);
            }

            /// <summary>
            /// Разпаковка
            /// </summary>
            /// <param name="array">массив для распаковки</param>
            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }
#pragma warning disable CS1591 // (я заебусь это коментировать...) (сгенерировано ChatGPT-3.5) Отсутствует комментарий XML для открытого видимого типа или члена
        public sealed class FuncOut<TOut, T1, T2> : AIndicator
        {
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task<TOut> RCall(T1 t1, T2 t2, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

            protected internal override int Size => DPackUp.CalcNeedSize(Dt1, Dt2);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1, T2> DPackUp = Packager.Create<T1, T2>();
            private T1 Dt1;
            private T2 Dt2;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2);
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }

        public sealed class FuncOut<TOut, T1, T2, T3> : AIndicator
        {
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task<TOut> RCall(T1 t1, T2 t2, T3 t3, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

            protected internal override int Size => DPackUp.CalcNeedSize(Dt1, Dt2, Dt3);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1, T2, T3> DPackUp = Packager.Create<T1, T2, T3>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3);
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }

        public sealed class FuncOut<TOut, T1, T2, T3, T4> : AIndicator
        {
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task<TOut> RCall(T1 t1, T2 t2, T3 t3, T4 t4, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

            protected internal override int Size => DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1, T2, T3, T4> DPackUp = Packager.Create<T1, T2, T3, T4>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4);
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }

        public sealed class FuncOut<TOut, T1, T2, T3, T4, T5> : AIndicator
        {
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task<TOut> RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

            protected internal override int Size => DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1, T2, T3, T4, T5> DPackUp = Packager.Create<T1, T2, T3, T4, T5>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5);
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }

        public sealed class FuncOut<TOut, T1, T2, T3, T4, T5, T6> : AIndicator
        {
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task<TOut> RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

            protected internal override int Size => DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1, T2, T3, T4, T5, T6> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6);
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }

        public sealed class FuncOut<TOut, T1, T2, T3, T4, T5, T6, T7> : AIndicator
        {
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task<TOut> RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                Dt7 = t7;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

            protected internal override int Size => DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1, T2, T3, T4, T5, T6, T7> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6, T7>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;
            private T7 Dt7;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7);
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }

        public sealed class FuncOut<TOut, T1, T2, T3, T4, T5, T6, T7, T8> : AIndicator
        {
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task<TOut> RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                Dt7 = t7;
                Dt8 = t8;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

            protected internal override int Size => DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1, T2, T3, T4, T5, T6, T7, T8> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;
            private T7 Dt7;
            private T8 Dt8;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8);
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }

        public sealed class FuncOut<TOut, T1, T2, T3, T4, T5, T6, T7, T8, T9> : AIndicator
        {
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task<TOut> RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, T9 t9, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                Dt7 = t7;
                Dt8 = t8;
                Dt9 = t9;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

            protected internal override int Size => DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8, Dt9);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1, T2, T3, T4, T5, T6, T7, T8, T9> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;
            private T7 Dt7;
            private T8 Dt8;
            private T9 Dt9;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8, Dt9);
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }

        public sealed class FuncOut<TOut, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : AIndicator
        {
            public FuncOut(string name)
            {
                ID = name.DeterministicGetHashCode();
#if DEBUG
                Name = name;
#endif
            }

            public async Task<TOut> RCall(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, T9 t9, T10 t10, AClient client, RCType type = RCType.ReturnWait, CancellationToken token = default)
            {
                Dt1 = t1;
                Dt2 = t2;
                Dt3 = t3;
                Dt4 = t4;
                Dt5 = t5;
                Dt6 = t6;
                Dt7 = t7;
                Dt8 = t8;
                Dt9 = t9;
                Dt10 = t10;
                Dtout = default;
                await RCallLow(client, type, token).ConfigureAwait(false);
                return Dtout;
            }

            protected internal override int Size => DPackUp.CalcNeedSize(Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8, Dt9, Dt10);

            private readonly static Packager.M<TOut> DUnPack = Packager.Create<TOut>();
            private TOut Dtout;
            private readonly static Packager.M<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> DPackUp = Packager.Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>();
            private T1 Dt1;
            private T2 Dt2;
            private T3 Dt3;
            private T4 Dt4;
            private T5 Dt5;
            private T6 Dt6;
            private T7 Dt7;
            private T8 Dt8;
            private T9 Dt9;
            private T10 Dt10;

            protected internal override void PackUp(INGCArray array)
            {
                DPackUp.PackUP(array.Bytes, array.Offset, Dt1, Dt2, Dt3, Dt4, Dt5, Dt6, Dt7, Dt8, Dt9, Dt10);
            }

            protected internal override void UnPack(INGCArray array)
            {
                DUnPack.UnPack(array.Bytes, array.Offset, out Dtout);
            }
        }
#pragma warning restore CS1591 // Отсутствует комментарий XML для открытого видимого типа или члена 
    }

}