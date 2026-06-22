namespace EMI
{
    internal static class Deterministic
    {
        /// <summary>
        /// Возвращает 64-битный хеш код строки текста (детерминированный — одинаковый для одной и той же строки на всех устройствах).
        /// Используется алгоритм FNV-1a 64-bit (Fowler–Noll–Vo).
        /// </summary>
        /// <param name="str">строка текста для которой необходимо расчитать хеш код</param>
        /// <returns>64-битный хеш код строки текста</returns>
        public static long DeterministicGetHashCode(this string str)
        {
            // FNV-1a 64-bit constants
            const long FNV_OFFSET_BASIS = unchecked((long)0xcbf29ce484222325);
            const long FNV_PRIME = 0x100000001b3;

            unchecked
            {
                long hash = FNV_OFFSET_BASIS;
                for (int i = 0; i < str.Length; i++)
                {
                    hash ^= str[i];
                    hash *= FNV_PRIME;
                }
                return hash;
            }
        }
    }
}
