using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.IO;

using Newtonsoft.Json;

namespace SimpleServer {

  static class MessageConstants {
    public const string Connect = "connect";
    public const string Disconnect = "disconnect";
  }

  public sealed class JsonMessage<T> {
    public string n;
    public T m;
  }

  public abstract class TcpMessageHandler {
    List<Tuple<string, RequestProcessorAsync>> handlers;
    JsonSerializer serializer;

    public delegate Task<bool> RequestProcessorAsync(object clientId, dynamic message);
    public delegate Task<bool> SpecializedRequestProcessorAsync<T>(object clientId, T message);

    internal TcpMessageHandler() {
      handlers = new List<Tuple<string, RequestProcessorAsync>>();
      serializer = new JsonSerializer();
    }

    protected async void RegisterClientAsync(TcpClient client) {
      var stream = client.GetStream();
      var receiveBufferSize = client.ReceiveBufferSize;
      var bytes = new byte[receiveBufferSize];
      while (true) {
        var sb = new StringBuilder();
        do {
          var amount = await stream.ReadAsync(bytes, 0, receiveBufferSize);
          sb.Append(Encoding.UTF8.GetString(bytes, 0, amount));
        } while (stream.DataAvailable);
        var str = sb.ToString();
        var message = serializer.Deserialize<JsonMessage<dynamic>>(new JsonTextReader(new StringReader(str)));
        ProcessMessageAsync(message.n, client, message.m);
      }
    }

    protected async void ProcessMessageAsync(string id, TcpClient client, dynamic message) {
      foreach (var handler in handlers) {
        if (handler.Item1 == null || handler.Item1 == id) {
          if (await handler.Item2(client, message)) break;
        }
      }
    }

    public void On(string id, RequestProcessorAsync processor) {
      handlers.Add(new Tuple<string, RequestProcessorAsync>(id, processor));
    }

    public void On<T>(string id, SpecializedRequestProcessorAsync<T> processor) {
      throw new NotImplementedException();
    }

    protected async Task SendTo(TcpClient client, string messageId, dynamic message) {
      var jsonMessage = GetJson(new { n = messageId, m = message });
      var bytes = Encoding.UTF8.GetBytes(jsonMessage);
      await client.GetStream().WriteAsync(bytes, 0, bytes.Length);
    }

    public abstract Task SendAsync(object clientId, string messageId, dynamic message);

    string GetJson(object message) {
      var writer = new StringWriter();
      serializer.Serialize(writer, message);
      return writer.ToString();
    }
  }

  public sealed class TcpMessageServer : TcpMessageHandler {
    TcpListener listener;
    List<TcpClient> clients;

    public TcpMessageServer(int port) {
      listener = new TcpListener(port);
      clients = new List<TcpClient>();
    }

    public void StartInForeground() {
      listener.Start();
      while (true) {
        var client = listener.AcceptTcpClient();
        clients.Add(client);
        ProcessMessageAsync(MessageConstants.Connect, client, null);
        RegisterClientAsync(client);
      }
    }

    public void StartInBackground() {
      new Thread(StartInForeground).Start();
    }

    public override async Task SendAsync(object clientId, string messageId, dynamic message) {
      if (clientId is TcpClient) {
        await SendTo((TcpClient)clientId, messageId, message);
      } else {
        throw new ArgumentException("Only use client IDs provided by TcpMessageServer", "clientId");
      }
    }

    public async Task Broadcast(string messageId, dynamic message) {
      foreach (var client in clients) {
        await SendTo(client, messageId, message);
      }
    }
  }

  public sealed class TcpMessageClient : TcpMessageHandler {
    TcpClient server;

    public TcpMessageClient(string serverHost, int serverPort) {
      server = new TcpClient(serverHost, serverPort);
    }

    public void Start() {
      RegisterClientAsync(server);
    }

    public override async Task SendAsync(object clientId, string messageId, dynamic message) {
      if (clientId != null) {
        throw new ArgumentException("Please use null to indicate sending to the server", "clientId");
      }
      await SendTo(server, messageId, message);
    }
  }

}
