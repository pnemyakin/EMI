# Справочник API

## Server

Пространство имён: `EMI`

Конструктор:

```csharp
Server(INetworkService service)
```

Принимает реализацию транспорта. Стандартные: `NetTCPV3Service.Service`, `NetUDPService.Service`.

### Свойства

| Свойство | Тип | Описание |
|---|---|---|
| `IsRun` | `bool` | Сервер запущен и принимает подключения |
| `RPC` | `RPC` | Глобальный реестр RPC-методов, общий для всех клиентов |
| `UseEncryption` | `bool` | Включить AES-256-GCM шифрование (устанавливать до `Start`) |
| `UseCompression` | `bool` | Включить LZ4 сжатие (устанавливать до `Start`) |
| `Middlewares` | `List<IPacketMiddleware>` | Ручная настройка цепочки middleware |
| `ServerClients` | `Client[]` | Массив подключённых клиентов (только DEBUG) |

### Методы

| Метод | Описание |
|---|---|
| `Start(string address)` | Запуск сервера. Формат: `IP#Port` |
| `Accept()` : `Task<Client>` | Ожидание нового подключения. Возвращает серверный объект `Client` |
| `Stop()` | Остановка сервера |

### События (только DEBUG)

| Событие | Описание |
|---|---|
| `OnClientConnect` | Клиент подключился |
| `OnClientDisconnect` | Клиент отключился |

## Client

Пространство имён: `EMI`

Конструктор для клиентской стороны:

```csharp
Client(INetworkService service)
```

Серверные объекты `Client` создаются автоматически при вызове `Server.Accept()`.

### Свойства

| Свойство | Тип | Описание |
|---|---|---|
| `IsConnect` | `bool` | Соединение активно |
| `IsServerSide` | `bool` | `true` для серверных объектов клиента |
| `Ping` | `TimeSpan` | Текущий RTT |
| `PingTimeout` | `TimeSpan` | Таймаут отключения при отсутствии ответа (по умолчанию 1 минута) |
| `PingPollingInterval` | `TimeSpan` | Интервал отправки ping (по умолчанию 15 секунд) |
| `MaxPacketAcceptSize` | `int` | Максимальный размер входящего пакета в байтах (по умолчанию 10 МБ) |
| `SendByteSpeed` | `double` | Скорость отправки (байт/сек) |
| `DeliveredRate` | `double` | Доля доставленных пакетов (0.0 .. 1.0) |
| `RemoteAddress` | `string` | Адрес удалённой стороны |
| `LocalRPC` | `RPC` | Локальный реестр RPC (методы только этого клиента) |
| `RPC` | `RPC` | Общий реестр RPC (на сервере равен `Server.RPC`, на клиенте равен `LocalRPC`) |
| `RandomDrop` | `RandomDropType` | Политика выборочного отбрасывания пакетов при перегрузке |
| `UseEncryption` | `bool` | Шифрование (устанавливать до `Connect`) |
| `UseCompression` | `bool` | Сжатие (устанавливать до `Connect`) |

### Методы

| Метод | Описание |
|---|---|
| `Connect(string address, CancellationToken token)` : `Task<bool>` | Подключение к серверу |
| `Disconnect(string reason)` | Отключение с указанием причины |

### События

| Событие | Описание |
|---|---|
| `Disconnected` | Вызывается при разрыве соединения. Передаёт строку с причиной |

## RPC

Реестр удалённых методов. Доступен через `Server.RPC`, `Client.LocalRPC`, `Client.RPC`.

### Методы

Регистрация метода без возвращаемого значения:

```csharp
IRPCRemoveHandle RegisterMethod(Action handler, Indicator.Func indicator)
IRPCRemoveHandle RegisterMethod<T1>(Action<T1> handler, Indicator.Func<T1> indicator)
// ... до 20 параметров
```

Регистрация метода с возвращаемым значением:

```csharp
IRPCRemoveHandle RegisterMethod<TOut>(Func<TOut> handler, Indicator.FuncOut<TOut> indicator)
IRPCRemoveHandle RegisterMethod<TOut, T1>(Func<T1, TOut> handler, Indicator.FuncOut<TOut, T1> indicator)
// ... до 10 параметров
```

Регистрация перенаправления:

```csharp
IRPCRemoveHandle RegisterForwarding(AIndicator indicator, Func<Client, Client[]> targetSelector)
```

`targetSelector` вызывается для каждого входящего forwarding-пакета. Получает клиента-отправителя, возвращает массив клиентов-получателей.

## Indicator

Пространство имён: `EMI`

Дескрипторы удалённых методов. Строковое имя хешируется через `Deterministic.DeterministicGetHashCode()` для получения целочисленного ID. Клиент и сервер должны использовать одинаковое имя для связывания.

### Indicator.Func

Вызов метода без возвращаемого значения:

```csharp
new Indicator.Func("MethodName")                          // 0 параметров
new Indicator.Func<string>("MethodName")                   // 1 параметр
new Indicator.Func<int, float, string>("MethodName")       // 3 параметра
// ... до 20 параметров
```

Вызов:

```csharp
await indicator.RCall(client, RCType.Guaranteed);                          // 0 параметров
await indicator.RCall("значение", client, RCType.Guaranteed);              // 1 параметр
await indicator.RCall(42, 3.14f, "текст", client, RCType.Guaranteed);     // 3 параметра
```

### Indicator.FuncOut

Вызов метода с возвращаемым значением:

```csharp
new Indicator.FuncOut<DateTime>("GetTime")                              // 0 параметров
new Indicator.FuncOut<bool, string>("Authenticate")                     // 1 параметр
// ... до 10 параметров
```

Вызов:

```csharp
DateTime time = await indicator.RCall(client, RCType.ReturnWait);
bool ok = await indicator.RCall("token", client, RCType.ReturnWait);
```

### Важно

Один экземпляр `Indicator` не является потокобезопасным для одновременных вызовов `RCall`. Параметры хранятся в полях экземпляра. Для параллельных вызовов используйте отдельные экземпляры или синхронизацию.

## RCType

| Значение | Гарантия | Блокирует | Описание |
|---|---|---|---|
| `Fast` | Нет | Нет | Отправить и забыть |
| `Guaranteed` | Да | Нет | Гарантированная доставка без ответа |
| `ReturnWait` | Да | Да (async) | Ожидание возвращаемого значения |
| `Forwarding` | Да | Нет | Перенаправление через сервер |
| `FastForwarding` | Нет | Нет | Ненадёжное перенаправление |

## SyncInterface\<T\>

Пространство имён: `EMI.SyncInterface`

```csharp
SyncInterface<T>(string name) where T : class
```

### Методы

| Метод | Описание |
|---|---|
| `RegisterClass(Server server, T implementation)` | Регистрация реализации на сервере |
| `RegisterClass(Client client, T implementation)` | Регистрация реализации на клиенте |
| `NewIndicator(Client client)` : `T` | Создание прокси для вызова методов на удалённой стороне |

### Атрибуты для интерфейсов

| Атрибут | Описание |
|---|---|
| `[OnlyServer]` | Метод вызывается только от клиента к серверу |
| `[OnlyClient]` | Метод вызывается только от сервера к клиенту |
| `[RCTypeOption(RCType)]` | Переопределение типа доставки |
| `[AsyncCompile]` | Пометка для асинхронной компиляции |

### Поддерживаемые сигнатуры

```csharp
void Method();                        // синхронный, без результата
int Method();                         // синхронный, с результатом
Task Method();                        // асинхронный, без результата
Task<int> Method();                   // асинхронный, с результатом
void Method(int a, string b);         // с параметрами
Task<bool> Method(string token);      // асинхронный, с параметрами и результатом
```

Интерфейс не должен содержать свойства. Только методы.

## HeadlessHandler

Пространство имён: `EMI.Headless`

### Свойства

| Свойство | Тип | Описание |
|---|---|---|
| `RPC` | `RPC` | Реестр RPC-методов |
| `SendPacket` | `Action<byte[]>` | Делегат отправки пакета (устанавливается пользователем) |
| `Middlewares` | `List<IPacketMiddleware>` | Цепочка middleware |

### Методы

| Метод | Описание |
|---|---|
| `AcceptPacket(byte[] data)` | Обработка входящего пакета |
| `RPCRun()` | Синхронная обработка очереди RPC |

## IPacketMiddleware

Интерфейс для пользовательских middleware.

```csharp
public interface IPacketMiddleware
{
    int Id { get; }
    int Version { get; }
    byte[] ProcessOutgoing(byte[] data);
    byte[] ProcessIncoming(byte[] data);
}
```

При отправке middleware применяются в прямом порядке. При получении в обратном.

## NGCArray

Пространство имён: `EMI.NGC`

Реализует `INGCArray` и `IDisposable`. Массив из пула. Создаётся через конструктор с указанием размера. При `Dispose()` возвращается в пул.

```csharp
using var array = new NGCArray(1024);
// array.Bytes — byte[]
// array.Length — логическая длина
// array.Offset — текущая позиция чтения/записи
```

Свойства пула (только DEBUG):

| Свойство | Тип | Описание |
|---|---|---|
| `NGCArray.UseArrays` | `int` | Количество массивов в использовании |
| `NGCArray.TotalUseSize` | `long` | Суммарный объём используемой памяти |
| `NGCArray.FreeArraysCount` | `int` | Количество свободных массивов в пуле |
| `NGCArray.TotalFreeArraysSize` | `long` | Суммарный объём свободных массивов |

## Исключения

Пространство имён: `EMI.MyException`

| Исключение | Описание |
|---|---|
| `AlreadyException` | Повторное действие (запуск запущенного сервера, подключение подключённого клиента) |
| `ClientDisconnectException` | Клиент отключился во время операции |
| `ClientViolationRightsException` | Нарушение прав клиента (приводит к отключению) |
| `RegisterLimitException` | Превышен лимит RPC-регистраций (65536) |
| `RPCRegisterNameException` | Дублирование имени RPC-метода |

Пространство имён: `EMI.SyncInterface`

| Исключение | Описание |
|---|---|
| `InvalidInterfaceException` | Тип не является интерфейсом или содержит свойства |

## PacketType

Внутренние типы пакетов:

| Значение | Описание |
|---|---|
| `Ping_Send` | Запрос пинга |
| `Ping_Receive` | Ответ на пинг |
| `RPC_Simple` | Обычный RPC-вызов (void) |
| `RPC_Return` | RPC-вызов с запросом результата |
| `RPC_Returned` | Ответ с результатом RPC |
| `RPC_Forwarding` | Перенаправляемый вызов |

## Транспортные сервисы

### NetTCPV3Service

```csharp
NetTCPV3Service.Service  // INetworkService для TCP
```

Сегментация пакетов, предвыделенные буферы заголовков, `NoDelay = true`.

### NetUDPService

```csharp
NetUDPService.Service  // INetworkService для UDP
```

MTU: 1200 байт. Надёжная доставка с окном 32 пакета. Ретрансмиссия: 100ms..1000ms, до 15 попыток. Таймаут фрагментов: 10 секунд. Таймаут соединения: 10 секунд тишины.

## Debugger API

### EDebuggerServer

Пространство имён: `EMI.DebugServer`

```csharp
var debugServer = new EDebuggerServer(Server server);
```

Создаёт отдельный EMI-сервер для подключения отладчика. Требует DEBUG-сборку EMI.

Передаёт в реальном времени: состояние сервера, информацию о клиентах, список RPC-методов, состояние NGC, логи.

### Структуры данных отладчика

| Структура | Содержимое |
|---|---|
| `ServerInfo` | Состояние и адрес сервера |
| `ClientInfo` | Адрес, пинг, скорость, состояние клиента |
| `RPCInfo` | Имя, ID, тип зарегистрированного метода |
| `NGCInfo` | Количество и объём массивов в пуле |
| `MSG` | Сообщение лога (тип, текст) |
| `AllInfo` | Полный снимок всех данных при подключении отладчика |
