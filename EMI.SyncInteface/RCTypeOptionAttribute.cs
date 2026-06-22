using System;

namespace EMI.SyncInterface
{
    using EMI;

    /// <summary>
    /// Переопределяет тип удалённого вызова (<see cref="RCType"/>) для конкретного метода интерфейса SyncInterface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// По умолчанию методы без возвращаемого значения (<c>void</c> / <c>Task</c>) используют <see cref="RCType.Fast"/>,
    /// а методы с возвращаемым значением (<c>Task&lt;T&gt;</c>, FuncOut) — <see cref="RCType.ReturnWait"/>.
    /// </para>
    /// <para><b>Предупреждение: FuncOut + Guaranteed/Fast</b><br/>
    /// Если метод возвращает <c>Task&lt;T&gt;</c> (FuncOut), задавать <see cref="RCType.Guaranteed"/> или
    /// <see cref="RCType.Fast"/> <b>бессмысленно и приводит к ошибке</b>: оба режима не ожидают ответа от удалённой стороны,
    /// поэтому вызывающий получит <c>default(T)</c> вместо реального результата.
    /// Используйте этот атрибут на <c>Task&lt;T&gt;</c>-методах только с <see cref="RCType.ReturnWait"/>,
    /// либо не ставьте атрибут вовсе — <see cref="RCType.ReturnWait"/> является значением по умолчанию для FuncOut.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public class RCTypeOptionAttribute : Attribute
    {
        /// <summary>Тип удалённого вызова для данного метода.</summary>
        public RCType RCType;

        /// <summary>
        /// Задать тип удалённого вызова для метода.
        /// </summary>
        /// <param name="type">Желаемый <see cref="RCType"/>. См. замечания класса о совместимости с FuncOut.</param>
        public RCTypeOptionAttribute(RCType type)
        {
            RCType = type;
        }
    }
}