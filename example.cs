using SimpleServer;

class ServerExample {
  static void Main(string[] args) {
    var server = new Server(3000);
    server.Use("/", (context) => {
      context.End("Hello world!");
      return true;
    });
    server.Use("/say/hi", (context) => {
      context.Write("Hi :)");
      return false;
    });
    server.Use("/say/:message", (context) => {
      context.End(context.GetParam("message"));
      return true;
    });
    server.Use("/say/:message/to/:name", (context) => {
      context.End(context.GetParam("message") + " " + context.GetParam("name"));
      return true;
    });
    server.Use((context) => {
      context.End("no handler found!");
      return true;
    });
    server.StartInForeground();
  }
}
