using EMI;
using EMI.Network.NetTCPV3;
using EMI.NetStream;
using System.Diagnostics;

Client client;

static void Client_Disconnected(string error)
{
    Console.WriteLine("Client_Disconnected => " + error);
}

//client
if (args.Length == 0 || args[0].Trim().ToLower() == "client")
{
    //if (args.Length != 0)
    //    System.Diagnostics.Process.Start("server.bat");

    client = new(NetTCPV3Service.Service);
    client.Disconnected += Client_Disconnected;
reconect:
    Console.WriteLine("Попытка подключиться...");//"31.10.114.169#25566"
    var status = client.Connect("127.0.0.1#25566", default).Result;
    if (status == false)
    {
        Console.WriteLine("Не удалось подключиться...");
        goto reconect;
    }
    Console.WriteLine("Успех, нажмите что бы продолжить");
    Console.ReadLine();
    Console.WriteLine("Попытка скачать файл");
    var f = new FileInfo("ня.png");
    if (f.Exists)
    {
        f.Delete();
    }
    var file = File.Create("ня.png");
    Stopwatch stopwatch = new Stopwatch();
    stopwatch.Start();
    int pp=-1;
    FileDownloader.Download(client, 0, "ня!", file, (info) =>
    {
        int p = (int)info.Progress / 5;
        if (p != pp)
        {
            pp = p;
            Console.WriteLine("===============\n" + info.ToString());
        }
    }, 1024 * 1024).Wait();
    stopwatch.Stop();
    Console.WriteLine("Файл загружен! "+ (file.Length / 1024 / 1024 / stopwatch.Elapsed.TotalSeconds) + "MB/s");
    file.Close();
}//server
else
{
    Server server = new Server(NetTCPV3Service.Service);
    Console.WriteLine("Нажмите кнопку что бы запустить сервер");
    Console.ReadLine();

    server.Start("any#25566");
    Console.WriteLine("Ожидание клиента");
rep:
    try
    {
        client = server.Accept().Result;
        client.Disconnected += Client_Disconnected;
        Console.WriteLine("Готово");

        FilesHost host = new FilesHost(client, 0, (string name) =>
        {
            Console.WriteLine("Файл? -> " + name);
            if (name == "ня!")
            {
                return File.OpenRead("test.png");
            }
            else
            {
                return null;
            }
        });

        Console.ReadLine();
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
        goto rep;
    }
}