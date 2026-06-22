using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EMI.SyncInterface
{
    using Indicators;

    internal static partial class Utils
    {
        /// <summary>
        /// Возвращает true, если последний параметр метода — CancellationToken.
        /// Такие параметры трактуются как токен отмены RPC, а не сериализуемые данные.
        /// </summary>
        public static bool HasTrailingCancellationToken(this MethodInfo method)
        {
            var parameters = method.GetParameters();
            return parameters.Length > 0 && parameters[parameters.Length - 1].ParameterType == typeof(CancellationToken);
        }

        /// <summary>
        /// Возвращает типы параметров для RPC-данных (исключая завершающий CancellationToken, если он есть).
        /// </summary>
        public static Type[] GetRpcParametersType(this MethodInfo method)
        {
            var all = method.GetParametersType();
            if (method.HasTrailingCancellationToken())
            {
                var result = new Type[all.Length - 1];
                Array.Copy(all, result, result.Length);
                return result;
            }
            return all;
        }

        public static ModuleBuilder InitModuleBuilder()
        {
            SmartPackager.PackMethods.SetupAgainPackMethods(); //иначе smart packager будет пытаться просканировать несозданные типы и крашнет всю прогу
            AssemblyName aName = new AssemblyName("EMI.DynamicCodeGenerate.SyncInterface");
            AssemblyBuilder aBuilder = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run);

            return aBuilder.DefineDynamicModule(aName.Name);
        }

        public static MethodInfo FindMethod(this MethodInfo[] methods, string name)
        {
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == name)
                    return methods[i];
            }
            throw new KeyNotFoundException();
        }

        /// <summary>
        /// Ищит и возвращает подходящий индикатор для метода
        /// </summary>
        /// <param name="method">метод для которого ищится индикатор</param>
        /// <returns>индикатор (может быть generic (надо создать))</returns>
        /// <exception cref="KeyNotFoundException">метод не найден</exception>
        public static Type GetIndicatorFunc(MethodInfo method)
        {
            var classes = typeof(Indicator).GetNestedTypes();
            string name;
            if (method.IsReturnData())
                name = nameof(Indicator.FuncOut<int>);
            else
                name = nameof(Indicator.Func);

            int gen = method.GetRpcParametersType().Length;

            if (method.ReturnType != typeof(void) && method.ReturnType != typeof(Task))
                gen++;

            if (gen > 0)
                name += "`" + gen;

            for (int i = 0; i < classes.Length; i++)
            {
                if (classes[i].Name == name)
                    return classes[i];
            }
            throw new KeyNotFoundException();
        }

        public static MethodInfo FindMethodPro(this Type type,string name, Type[] types)
        {
            foreach(var method in type.GetMethods())
            {
                if (method.Name == name)
                {
                    var param = method.GetParametersType();
                    if (param.Length == types.Length)
                    {
                        for (int i = 0; i < param.Length; i++)
                        {
                            if (param[i].Name != types[i].Name)
                                goto skip;
                        }
                        return method;
                    }
                }
            skip:;
            }
            throw new KeyNotFoundException();
        }

        public static bool IsReturnData(this MethodInfo method)
        {
            var type = method.ReturnType;
            return type != typeof(void) && type != typeof(Task);
        }

        public static Type GetReturnType(this MethodInfo method)
        {
            if (method.IsAsync(true))
                return method.ReturnType.GenericTypeArguments[0];
            else
                return method.ReturnType;
        }

        public static Type[] GetReturnAndParametrs(this MethodInfo method)
        {
            Type[] types;
            var rpcParams = method.GetRpcParametersType();
            if (method.IsReturnData())
            {
                types = new Type[rpcParams.Length + 1];
                types[0] = method.GetReturnType();

                for (int i = 0; i < rpcParams.Length; i++)
                    types[i + 1] = rpcParams[i];
            }
            else
            {
                types = new Type[rpcParams.Length];
                for (int i = 0; i < rpcParams.Length; i++)
                    types[i] = rpcParams[i];
            }
            return types;
        }

        /// <summary>
        /// Создаёт generic метод
        /// </summary>
        /// <param name="indicator">индикатор который необходимо создать</param>
        /// <param name="method">метод для которого создаётся индикатор</param>
        /// <returns></returns>
        public static Type CreateGenericIndicator(Type indicator, MethodInfo method)
        {
            return indicator.MakeGenericType(method.GetReturnAndParametrs());
        }

        public static string FieldNameCreate(ref int id)
        {
            return $"SyncInterface::Field#[{id++}]";
        }

        public static bool IsAsync(this MethodInfo method, bool onlyTaskReturn = false)
        {
            var rp = method.ReturnParameter.ParameterType;
            return (rp == typeof(Task) && !onlyTaskReturn) || rp.BaseType == typeof(Task);
        }

        public static Type[] GetParametersType(this MethodInfo method)
        {
            var mParametersInfo = method.GetParameters();
            var mParameters = new Type[mParametersInfo.Length];
            for (int i = 0; i < mParametersInfo.Length; i++)
                mParameters[i] = mParametersInfo[i].ParameterType;
            return mParameters;
        }

// MakeRPCDelegate moved to Utils.Generated.cs (generated from Utils.Generated.tt)

        /// <summary>
        /// Создаёт RPCfunc делегат-обёртку для async метода, возвращающего Task (без результата).
        /// Обёртка: () => method(...).GetAwaiter().GetResult()
        /// </summary>
        public static Delegate MakeRPCDelegateAsyncVoid(Type[] inTypes, object context, MethodInfo mi)
        {
            var targetConst = Expression.Constant(context);
            var paramExprs = new ParameterExpression[inTypes.Length];
            for (int i = 0; i < inTypes.Length; i++)
                paramExprs[i] = Expression.Parameter(inTypes[i], "p" + i);

            // Если метод ожидает CancellationToken последним параметром, добавляем default(CancellationToken)
            Expression[] callArgs;
            if (mi.HasTrailingCancellationToken())
            {
                callArgs = new Expression[paramExprs.Length + 1];
                Array.Copy(paramExprs, callArgs, paramExprs.Length);
                callArgs[paramExprs.Length] = Expression.Default(typeof(CancellationToken));
            }
            else
            {
                callArgs = paramExprs;
            }

            var callExpr = Expression.Call(targetConst, mi, callArgs);

            var getAwaiterMethod = typeof(Task).GetMethod(nameof(Task.GetAwaiter));
            var awaiterType = typeof(TaskAwaiter);
            var getResultMethod = awaiterType.GetMethod(nameof(TaskAwaiter.GetResult));

            var awaiterVar = Expression.Variable(awaiterType, "awaiter");
            var bodyBlock = Expression.Block(
                new[] { awaiterVar },
                Expression.Assign(awaiterVar, Expression.Call(callExpr, getAwaiterMethod)),
                Expression.Call(awaiterVar, getResultMethod)
            );

            Type delegateType = GetRPCfuncDelegateType(inTypes);
            return Expression.Lambda(delegateType, bodyBlock, paramExprs).Compile();
        }

        /// <summary>
        /// Создаёт RPCfuncOut делегат-обёртку для async метода, возвращающего Task&lt;T&gt;.
        /// Обёртка: () => method(...).GetAwaiter().GetResult() → T
        /// </summary>
        public static Delegate MakeRPCDelegateAsyncOut(Type returnType, Type[] inTypes, object context, MethodInfo mi)
        {
            var targetConst = Expression.Constant(context);
            var paramExprs = new ParameterExpression[inTypes.Length];
            for (int i = 0; i < inTypes.Length; i++)
                paramExprs[i] = Expression.Parameter(inTypes[i], "p" + i);

            // Если метод ожидает CancellationToken последним параметром, добавляем default(CancellationToken)
            Expression[] callArgs;
            if (mi.HasTrailingCancellationToken())
            {
                callArgs = new Expression[paramExprs.Length + 1];
                Array.Copy(paramExprs, callArgs, paramExprs.Length);
                callArgs[paramExprs.Length] = Expression.Default(typeof(CancellationToken));
            }
            else
            {
                callArgs = paramExprs;
            }

            var callExpr = Expression.Call(targetConst, mi, callArgs);

            var taskType = typeof(Task<>).MakeGenericType(returnType);
            var getAwaiterMethod = taskType.GetMethod(nameof(Task.GetAwaiter));
            var awaiterType = typeof(TaskAwaiter<>).MakeGenericType(returnType);
            var getResultMethod = awaiterType.GetMethod("GetResult");

            var awaiterVar = Expression.Variable(awaiterType, "awaiter");
            var bodyBlock = Expression.Block(
                returnType,
                new[] { awaiterVar },
                Expression.Assign(awaiterVar, Expression.Call(callExpr, getAwaiterMethod)),
                Expression.Call(awaiterVar, getResultMethod)
            );

            var types = new Type[inTypes.Length + 1];
            types[0] = returnType;
            Array.Copy(inTypes, 0, types, 1, inTypes.Length);
            Type delegateType = GetRPCfuncOutDelegateType(types);
            return Expression.Lambda(delegateType, bodyBlock, paramExprs).Compile();
        }

        /// <summary>
        /// Создаёт RPCfunc делегат-обёртку для синхронного void метода с завершающим CancellationToken.
        /// Обёртка: (p0, ..., pN-1) => method(p0, ..., pN-1, default(CancellationToken))
        /// </summary>
        public static Delegate MakeRPCDelegateWithTrailingCT(Type[] rpcTypes, object context, MethodInfo mi)
        {
            var targetConst = Expression.Constant(context);
            var paramExprs = new ParameterExpression[rpcTypes.Length];
            for (int i = 0; i < rpcTypes.Length; i++)
                paramExprs[i] = Expression.Parameter(rpcTypes[i], "p" + i);

            var callArgs = new Expression[paramExprs.Length + 1];
            Array.Copy(paramExprs, callArgs, paramExprs.Length);
            callArgs[paramExprs.Length] = Expression.Default(typeof(CancellationToken));

            var callExpr = Expression.Call(targetConst, mi, callArgs);
            Type delegateType = GetRPCfuncDelegateType(rpcTypes);
            return Expression.Lambda(delegateType, callExpr, paramExprs).Compile();
        }

        /// <summary>
        /// Создаёт RPCfuncOut делегат-обёртку для синхронного метода с возвратным значением и завершающим CancellationToken.
        /// Обёртка: (p0, ..., pN-1) => method(p0, ..., pN-1, default(CancellationToken))
        /// </summary>
        public static Delegate MakeRPCDelegateOutWithTrailingCT(Type returnType, Type[] rpcTypes, object context, MethodInfo mi)
        {
            var targetConst = Expression.Constant(context);
            var paramExprs = new ParameterExpression[rpcTypes.Length];
            for (int i = 0; i < rpcTypes.Length; i++)
                paramExprs[i] = Expression.Parameter(rpcTypes[i], "p" + i);

            var callArgs = new Expression[paramExprs.Length + 1];
            Array.Copy(paramExprs, callArgs, paramExprs.Length);
            callArgs[paramExprs.Length] = Expression.Default(typeof(CancellationToken));

            var callExpr = Expression.Call(targetConst, mi, callArgs);
            var types = new Type[rpcTypes.Length + 1];
            types[0] = returnType;
            Array.Copy(rpcTypes, 0, types, 1, rpcTypes.Length);
            Type delegateType = GetRPCfuncOutDelegateType(types);
            return Expression.Lambda(delegateType, callExpr, paramExprs).Compile();
        }

// GetRPCfuncDelegateType, GetRPCfuncOutDelegateType, MakeRPCDelegateOut moved to Utils.Generated.cs
    }
}