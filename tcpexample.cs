using System;
using System.Threading.Tasks;

using SimpleServer;

class TcpExampleServer {
  static void Main(string[] args) {
    Start();
    TcpExampleClient.Start();
    Console.WriteLine("Ctrl+C to stop");
    while (true) {
      System.Threading.Thread.Sleep(100);
    }
  }

  static void Start() {
    var server = new TcpMessageServer(12354);
    server.On("connect", async (c, m) => {
      Console.WriteLine("client connected to server");
      await server.SendAsync(c, "test", "hello");
      return true;
    });
    server.On("tick", async (c, m) => {
      Console.WriteLine("tick");
      return true;
    });
    server.StartInBackground();
  }
}

class TcpExampleClient {
  public static void Start() {
    var client = new TcpMessageClient("localhost", 12354);
    client.On("test", async (c, m) => {
      Console.WriteLine("client received message: {0}", m);
      Task.Factory.StartNew(async () => {
        while (true) {
          await client.SendAsync(null, "tick", null);
          await Task.Delay(1000);
        }
      });
      return true;
    });
    client.Start();
  }
}
