using System;
using System.Net;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SimpleServer {

  internal class ServerContextSharedState {
    StringBuilder responseBuilder;
    readonly HttpListenerContext listenerContext;

    public HttpListenerContext ListenerContext {
      get {
        return listenerContext;
      }
    }

    public ServerContextSharedState(HttpListenerContext listenerContext) {
      this.listenerContext = listenerContext;
    }

    void WriteResponseAndClose(string text) {
      var response = ListenerContext.Response;
      var bytes = response.ContentEncoding.GetBytes(text);
      response.ContentLength64 = bytes.Length;
      response.Close(bytes, false);
    }

    public void Write(string text) {
      if (responseBuilder == null) {
        responseBuilder = new StringBuilder();
      }
      responseBuilder.Append(text);
    }

    public void End(string text) {
      if (responseBuilder != null) {
        responseBuilder.Append(text);
        text = responseBuilder.ToString();
        responseBuilder = null;
      }
      WriteResponseAndClose(text);
    }

    public void End() {
      if (responseBuilder != null) {
        WriteResponseAndClose(responseBuilder.ToString());
        responseBuilder = null;
      }
      else {
        ListenerContext.Response.Close();
      }
    }
  }

  class ServerContext {
    readonly ServerContextSharedState sharedState;
    readonly Match regexMatch;

    public HttpListenerContext ListenerContext {
      get {
        return sharedState.ListenerContext;
      }
    }

    public HttpListenerRequest Request {
      get {
        return sharedState.ListenerContext.Request;
      }
    }

    public HttpListenerResponse Response {
      get {
        return sharedState.ListenerContext.Response;
      }
    }

    internal ServerContext(ServerContextSharedState sharedState, Match regexMatch) {
      this.sharedState = sharedState;
      this.regexMatch = regexMatch;
    }

    public string GetParam(string name) {
      if (regexMatch == null) {
        return null;
      }
      var group = regexMatch.Groups[name];
      if (group == null) {
        return null;
      }
      return group.Value;
    }

    public void Write(string text) {
      sharedState.Write(text);
    }

    public void End() {
      sharedState.End();
    }

    public void End(string text) {
      sharedState.End(text);
    }
  }

  class Server {
    HttpListener listener;

    // The return value tells if you handled the request. If you return true,
    // the request is considered done and no further processing will take place.
    // If you return false, additional handlers may be called.
    // If no handlers return true, it is assumed that the request was not handled
    // and the default error handler will be called. If you don't want this
    // behavior, add your own error handler that returns true after all other
    // routes have been added.
    public delegate bool RequestProcessor(ServerContext context);

    List<Tuple<Regex, RequestProcessor>> routes;
    Regex urlParamRegex;

    public Server(int port) {
      listener = new HttpListener();
      listener.Prefixes.Add("http://+:" + port + "/");

      routes = new List<Tuple<Regex, RequestProcessor>>();

      // match ":identifier" preceded by "/".
      urlParamRegex = new Regex(@"(?<=/):([a-zA-Z0-9_$-\.]+)", RegexOptions.Compiled);
    }

    public void StartInForeground() {
      listener.Start();
      while (true) {
        ThreadPool.QueueUserWorkItem(ProcessRequest, listener.GetContext());
      }
    }

    public void StartInBackground() {
      new Thread(StartInForeground).Start();
    }

    public void Use(RequestProcessor proc) {
      Use((Regex)null, proc);
    }

    // Use :name to capture param values.
    // Name must fit in the following regex: [a-zA-Z0-9_$-\.]+
    public void Use(string path, RequestProcessor proc) {
      Use(GetPathRegex(path), proc);
    }

    public void Use(Regex regex, RequestProcessor proc) {
      routes.Add(new Tuple<Regex, RequestProcessor>(regex, proc));
    }

    Regex GetPathRegex(string path) {
      path = Regex.Escape(path);
      path = urlParamRegex.Replace(path, @"(?<$1>[^/]+)");
      path = "^" + path + "$";
      return new Regex(path, RegexOptions.Compiled);
    }

    private static void DefaultErrorProcessor(HttpListenerContext context) {
      var response = context.Response;
      var responseString = "An error occurred :(";
      var encoding = System.Text.Encoding.UTF8;
      response.ContentEncoding = encoding;
      response.Close(encoding.GetBytes(responseString), false);
    }

    private void ProcessRequest(object threadContext) {
      var context = (HttpListenerContext)threadContext;

      var request = context.Request;
      var response = context.Response;

      // Set default encoding:
      response.ContentEncoding = System.Text.Encoding.UTF8;

      var urlWithoutQuery = request.RawUrl;
      var queryPos = urlWithoutQuery.IndexOf('?');
      if (queryPos != -1) {
        urlWithoutQuery = urlWithoutQuery.Substring(0, queryPos);
      }

      var sharedState = new ServerContextSharedState(context);

      foreach (var tup in routes) {
        Match match;
        if (tup.Item1 == null) {
          match = null;
        } else {
          match = tup.Item1.Match(urlWithoutQuery);
        }
        if (match == null || match.Success) {
          if (tup.Item2(new ServerContext(sharedState, match))) {
            return;
          }
        }
      }

      DefaultErrorProcessor(context);
    }
  }
}
