using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Threading;

namespace Spindle.Interop;

/// <summary>
/// Tek kopya çalışır. Sağ tık menüsü her seçili dosya için ayrı bir kopya başlatır; bu kopyalar
/// argümanlarını adlandırılmış boru ile ana kopyaya iletip hemen kapanır.
/// </summary>
static class SingleInstance
{
    static readonly string Name = "Spindle-" + Environment.UserName;
    static Mutex? mutex;

    sealed record Message(string Cwd, string[] Args);

    public static bool TryBecomePrimary()
    {
        mutex = new Mutex(initiallyOwned: true, @"Local\" + Name, out var created);
        return created;
    }

    public static bool Forward(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            client.Connect(2000);
            JsonSerializer.Serialize(client, new Message(Environment.CurrentDirectory, args));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Listen(Dispatcher dispatcher, Action<string[]> onArgs)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1);
                    server.WaitForConnection();
                    var message = JsonSerializer.Deserialize<Message>(server);
                    if (message is null) continue;
                    var args = message.Args
                        .Select(a => a.StartsWith("--") ? a : Path.GetFullPath(Path.Combine(message.Cwd, a)))
                        .ToArray();
                    dispatcher.BeginInvoke(() => onArgs(args));
                }
                catch (Exception)
                {
                    Thread.Sleep(200);
                }
            }
        })
        { IsBackground = true, Name = "Spindle boru" };
        thread.Start();
    }
}
