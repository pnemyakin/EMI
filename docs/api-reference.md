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
| `UseEncryption` | `bool` | Включить AES-256-GCM шифрование (устанавливать до `Start`). Ключ согласуется через `KeyExchange`, по сети открытым текстом не идёт |
| `UseCompression` | `bool` | Включить LZ4 сжатие (устанавливать до `Start`) |
| `KeyExchange` | `IKeyExchange` | Стратегия согласования ключа (лестница уровней, см. [security.md](security.md)). По умолчанию при `UseEncryption=true` — `RsaKeyExchange.CreateServer()` (уровень 2) |
| `Dispatcher` | `IRpcDispatcher` | Стратегия исполнения RPC-обработчиков (поток/порядок). Наследуется всеми клиентами. По умолчанию `ThreadPoolDispatcher`. Для Unity — `PumpDispatcher` |
| `Middlewares` | `List<IPacketMiddleware>` | Ручная настройка цепочки middleware (продвинутый режим, handshake не выполняется) |
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
| `KeyExchange` | `IKeyExchange` | Стратегия обмена ключом; должна соответствовать ступени сервера. null + `UseEncryption` → RSA inline (см. [security.md](security.md)) |
| `Dispatcher` | `IRpcDispatcher` | Стратегия исполнения RPC-обработчиков (поток/порядок). По умолчанию `ThreadPoolDispatcher`. Для Unity/движков — `PumpDispatcher`/`SynchronizationContextDispatcher` (см. [getting-started](getting-started.md)) |

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
// ... до 100 параметров
```

Регистрация метода с возвращаемым значением:

```csharp
IRPCRemoveHandle RegisterMethod<TOut>(Func<TOut> handler, Indicator.FuncOut<TOut> indicator)
IRPCRemoveHandle RegisterMethod<TOut, T1>(Func<T1, TOut> handler, Indicator.FuncOut<TOut, T1> indicator)
// ... до 100 параметров
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
// ... до 100 параметров
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
// ... до 100 параметров
```

Вызов:

```csharp
DateTime time = await indicator.RCall(client, RCType.ReturnWait);
bool ok = await indicator.RCall("token", client, RCType.ReturnWait);
```

### Важно

Один экземпляр `Indicator` **потокобезопасен** для одновременных вызовов `RCall` — параметры не хранятся в полях, а передаются через лямбду непосредственно в момент вызова.

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
| `[OnlyServer]` | Метод вызывается только от сервера к клиенту (server proxy → client handler) |
| `[OnlyClient]` | Метод вызывается только от клиента к серверу (client proxy → server handler) |
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
Task<byte[]> Method(int id, CancellationToken ct); // с CancellationToken
```

Если последний параметр метода — `CancellationToken`, он **не** сериализуется как данные, а пробрасывается в RPC-инфраструктуру для отмены вызова. На вызывающей стороне (прокси) токен передаётся в `RCall` и может отменить отправку или ожидание ответа. На принимающей стороне (реализация) обработчик получает `default(CancellationToken)`.

Интерфейс не должен содержать свойства. Только методы.

## HeadlessHandler

Пространство имён: `EMI.Headless`

`HeadlessHandler` — это **RPC-движок и middleware без встроенного транспорта и без модели соединения**.
Доставку байтов обеспечивает пользователь: задаёт делегат отправки и сам вызывает `AcceptPacket`.
Подходит для любого внешнего канала (Steam P2P, релеи, WebSocket, транспорт игрового движка,
in-memory очереди в тестах), а не только для Steam. Перенаправление (`RPC_Forwarding`) не поддерживается.

**Шифрование в Headless.** Автоматический обмен ключами (уровни 2–3 из [security.md](security.md))
здесь недоступен — для него нужен управляемый библиотекой handshake-раунд, которого у Headless нет.
Доступны только:
- **Уровень 1 (PSK):** передайте `new AesGcmMiddleware(key)` в `UseMiddleware(...)` — ключ роздан вне сети.
- **Уровень 0:** без шифрования, если внешний транспорт уже шифрует трафик (Steam P2P — да).

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

## RawBuffer

Пространство имён: `EMI.NGC`

Специальная структура для передачи бинарных данных через RPC **без GC-аллокаций на приёмной стороне**. В отличие от `byte[]`, который при каждом получении аллоцируется в куче, `RawBuffer` использует `NGCArray` — пул `ArrayPool<byte>.Shared`.

### Когда использовать `RawBuffer` вместо `byte[]`

| Сценарий | `byte[]` | `RawBuffer` |
|---|---|---|
| Низкочастотные вызовы | ✅ Ок | Избыточно |
| High-throughput (тысячи RPC/сек) | ❌ GC-давление | ✅ Пул, нет GC |
| Крупные блобы (мегабайты) | ❌ Фрагментация LOH | ✅ Пул |
| Детерминированная производительность | ❌ GC-паузы | ✅ Предсказуемо |

### Как устроен

**Отправка:** `PackRawBuffer : IPackagerMethod<RawBuffer>` — кастомный упаковщик SmartPackager. Данные пишутся напрямую через `Marshal.Copy`, без поэлементной сериализации. Формат на wire: `[int: длина] [byte: данные...]` (4 байта префикса).

**Получение:** при регистрации `RegisterMethod<...>` строится **dispose-чейн** — цепочка `RefFunc`-делегатов, по одному на каждый RawBuffer-параметр. После вызова обработчика цепочка автоматически освобождает все RawBuffer (возврат в `ArrayPool`). Построение цепочки происходит **один раз при регистрации**, в горячем пути — только `?.Invoke`.

**Multi-param:** работает полностью. `foo(int, RawBuffer, string, RawBuffer)` — оба RawBuffer будут освобождены.

### Отправка

```csharp
var indicator = new Indicator.Func<RawBuffer>("UploadFile");

using var buffer = new RawBuffer(4096);
buffer.Span[0] = 0x01;
// ...

await indicator.RCall(buffer, client, RCType.Guaranteed);
```

### Получение

```csharp
// Один параметр
rpc.RegisterMethod<RawBuffer>((RawBuffer buffer) =>
{
    var span = buffer.Span;
    // buffer авто-освобождается
}, indicator);

// Multi-param: оба RawBuffer авто-освобождаются
rpc.RegisterMethod<int, RawBuffer, string, RawBuffer>(
    (int id, RawBuffer data, string name, RawBuffer meta) =>
{
    // data и meta авто-освобождаются после выхода
}, indicator);
```

### ⚠️ RawBuffer как возвращаемое значение (TOut)

Если метод **возвращает** `RawBuffer`, авто-dispose **не** применяется — фреймворк не знает когда вы закончили чтение. Вы **обязаны** вызвать `Dispose()` самостоятельно:

```csharp
rpc.RegisterMethod<RawBuffer>((RawBuffer buffer) =>
{
    // Читаем данные
    var data = new byte[buffer.Length];
    Buffer.BlockCopy(buffer.Bytes, buffer.Offset, data, 0, buffer.Length);
    
    buffer.Dispose(); // ← ОБЯЗАТЕЛЬНО! Авто-dispose не для возвращаемых значений
    return data;
}, indicator);
```

### Свойства и методы

| Член | Тип | Описание |
|---|---|---|
| `RawBuffer(int size)` | конструктор | Выделить буфер из пула |
| `Bytes` | `byte[]` | Массив байт |
| `Offset` | `int` | Смещение начала полезных данных |
| `Length` | `int` | Длина полезных данных |
| `IsEmpty` | `bool` | Буфер пуст / освобождён |
| `Span` | `Span<byte>` | Span на полезные данные |
| `Memory` | `Memory<byte>` | Memory на полезные данные |
| `Dispose()` | метод | Освободить буфер (возврат в пул) |

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

## Подводные камни

### [RCTypeOption] + FuncOut возвращает default(T)

Если метод интерфейса `SyncInterface` возвращает `Task<T>` (или значение) и помечен атрибутом `[RCTypeOption(RCType.X)]` где `X != ReturnWait`, вызывающая сторона получит `default(T)` вместо реального результата — сервер не ждёт ответа.

В DEBUG-сборке выводится предупреждение в консоль при построении `SyncInterface<T>`. В Release — тихий баг.

```csharp
// Опасно — клиент получит false вместо реального результата аутентификации
[RCTypeOption(RCType.Guaranteed)]
Task<bool> Authenticate(string token);

// Правильно — RCType.ReturnWait используется по умолчанию для FuncOut
Task<bool> Authenticate(string token);
```

### SyncInterface: синхронные методы блокируют поток

Когда метод в интерфейсе объявлен как `void` или `T` (не `Task`), `SyncInterface` генерирует IL, который вызывает `.Wait()` или `.Result` на результирующей задаче. Это блокирует вызывающий поток.

Если вызов происходит из потока с `SynchronizationContext` (UI-поток WPF/WinForms, ASP.NET classic), возможен дедлок.

```csharp
// Риск дедлока при вызове из UI-потока:
void SendMessage(string msg);
int GetCount();

// Безопасно — явный async, управление возвратом вызывающей стороне:
Task SendMessage(string msg);
Task<int> GetCount();
```

### Client.RPC на серверной стороне — это глобальный реестр

Объект `Client`, полученный из `server.Accept()`, имеет `Client.RPC == Server.RPC`. Регистрация метода через `serverClient.RPC.RegisterMethod(...)` добавляет его в **глобальный** реестр, доступный всем подключённым клиентам.

Для методов, доступных только одному конкретному клиенту, используйте `Client.LocalRPC`:

```csharp
Client client = await server.Accept();

// Глобально — все клиенты смогут вызвать этот метод:
client.RPC.RegisterMethod(Handler, indicator);

// Только для этого клиента:
client.LocalRPC.RegisterMethod(Handler, indicator);
```

### Обработчики RPC поглощают исключения

Если зарегистрированный обработчик выбросит исключение, оно будет перехвачено, залогировано (только в DEBUG), а вызывающая сторона получит `default(T)`. Исключение не приходит к вызывающей стороне. Это поведение намеренное — один сбойный обработчик не роняет весь процесс.

### Лимит RPC-регистраций — 65536

Общее число зарегистрированных методов на один `RPC`-реестр ограничено 65536 (16-битный ID в пакете). При превышении выбрасывается `RegisterLimitException`. `Server.RPC` и `Client.LocalRPC` — независимые реестры с отдельными счётчиками.

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
