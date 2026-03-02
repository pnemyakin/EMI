using System;
using System.Text;

namespace EMI.DebugLog
{
    /// <summary>
    /// Сообщение об ошибке или событие.
    /// Предварительно разбирает format-строку при создании для быстрого форматирования.
    /// </summary>
    internal readonly struct LogMessage
    {
        /// <summary>
        /// Уровень критичности сообщения
        /// </summary>
        public readonly LogType Type;

        /// <summary>
        /// Исходная строка шаблона (поддерживает string format)
        /// </summary>
        public readonly string Message;

        /// <summary>
        /// Предварительно разобранные сегменты шаблона (текст между {N}).
        /// null если шаблон не содержит параметров.
        /// </summary>
        private readonly string[] _segments;

        /// <summary>
        /// Индексы аргументов ({0}, {1}, ...) в порядке появления.
        /// null если шаблон не содержит параметров.
        /// </summary>
        private readonly int[] _argIndices;

        /// <summary>
        /// Количество параметров в шаблоне
        /// </summary>
        public int ArgCount => _argIndices?.Length ?? 0;

        private LogMessage(LogType type, string message, string[] segments, int[] argIndices)
        {
            Type = type;
            Message = message;
            _segments = segments;
            _argIndices = argIndices;
        }

        /// <summary>
        /// Создать новое сообщение с предварительным разбором format-строки
        /// </summary>
        /// <param name="type">уровень критичности сообщения</param>
        /// <param name="message">сообщение об ошибке (поддерживает string format)</param>
        public static LogMessage Create(LogType type, string message)
        {
            Parse(message, out var segments, out var argIndices);
            return new LogMessage(type, message, segments, argIndices);
        }

        /// <summary>
        /// Форматировать сообщение, подставив аргументы.
        /// Использует предварительно разобранный шаблон — быстрее чем string.Format.
        /// </summary>
        public string Format(params object[] args)
        {
            if (_segments == null)
                return Message;

            // Оценка длины: сумма сегментов + ~16 символов на аргумент
            int estimatedLength = 0;
            for (int i = 0; i < _segments.Length; i++)
                estimatedLength += _segments[i].Length;
            estimatedLength += _argIndices.Length * 16;

            var sb = new StringBuilder(estimatedLength);
            for (int i = 0; i < _argIndices.Length; i++)
            {
                sb.Append(_segments[i]);
                int idx = _argIndices[i];
                if (idx < args.Length)
                    sb.Append(args[idx]?.ToString() ?? string.Empty);
            }
            sb.Append(_segments[_segments.Length - 1]);
            return sb.ToString();
        }

        /// <summary>
        /// Разбирает format-строку на сегменты и индексы аргументов.
        /// "Client => {0} => {1}" → segments=["Client => ", " => ", ""], argIndices=[0, 1]
        /// </summary>
        private static void Parse(string format, out string[] segments, out int[] argIndices)
        {
            // Быстрая проверка — если нет '{', шаблон без параметров
            if (format.IndexOf('{') < 0)
            {
                segments = null;
                argIndices = null;
                return;
            }

            // Максимум 10 параметров — для лог-сообщений более чем достаточно
            var segList = new string[11];
            var idxList = new int[10];
            int segCount = 0;
            int idxCount = 0;
            int pos = 0;

            while (pos < format.Length)
            {
                int bracePos = format.IndexOf('{', pos);
                if (bracePos < 0)
                    break;

                int endBrace = format.IndexOf('}', bracePos + 1);
                if (endBrace < 0)
                    break;

                // Сегмент текста до {N}
                segList[segCount++] = format.Substring(pos, bracePos - pos);

                // Парсим индекс аргумента
                int argIdx = 0;
                for (int c = bracePos + 1; c < endBrace; c++)
                {
                    char ch = format[c];
                    if (ch >= '0' && ch <= '9')
                        argIdx = argIdx * 10 + (ch - '0');
                }
                idxList[idxCount++] = argIdx;

                pos = endBrace + 1;
            }

            // Последний сегмент (после последнего {N})
            segList[segCount++] = pos < format.Length ? format.Substring(pos) : string.Empty;

            segments = new string[segCount];
            Array.Copy(segList, segments, segCount);

            argIndices = new int[idxCount];
            Array.Copy(idxList, argIndices, idxCount);
        }
    }
}