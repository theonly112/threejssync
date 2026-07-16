using System;
using System.IO;
using System.Linq;
using System.Text;
using CefSharp;
using CefSharp.Handler;
using ThreeJsSync.Core;

namespace ThreeJsSync.Host
{
    public sealed class LocalRequestHandler : RequestHandler
    {
        public const string Origin = "https://threejssync.local";
        private readonly string _webRoot;
        public event EventHandler<string> FetchMessage;

        public LocalRequestHandler(string webRoot) => _webRoot = Path.GetFullPath(webRoot);

        protected override IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme + "://" + uri.Host, Origin, StringComparison.OrdinalIgnoreCase)) return null;
            disableDefaultHandling = true;
            return new LocalResourceRequestHandler(_webRoot, HandleFetch);
        }

        private void HandleFetch(string json) => FetchMessage?.Invoke(this, json);
    }

    internal sealed class LocalResourceRequestHandler : ResourceRequestHandler
    {
        private readonly string _webRoot;
        private readonly Action<string> _fetch;
        public LocalResourceRequestHandler(string webRoot, Action<string> fetch) { _webRoot = webRoot; _fetch = fetch; }

        protected override IResourceHandler GetResourceHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request)
        {
            var uri = new Uri(request.Url);
            if (uri.AbsolutePath == "/sync/patch") return HandlePatch(request);
            if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)) return ResourceHandler.ForErrorMessage("Method not allowed", System.Net.HttpStatusCode.MethodNotAllowed);
            var relative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            if (string.IsNullOrEmpty(relative)) relative = "index.html";
            var fullPath = Path.GetFullPath(Path.Combine(_webRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(_webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                return ResourceHandler.ForErrorMessage("Not found", System.Net.HttpStatusCode.NotFound);
            return ResourceHandler.FromFilePath(fullPath, MimeType(fullPath), false);
        }

        private IResourceHandler HandlePatch(IRequest request)
        {
            if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)) return ResourceHandler.ForErrorMessage("Method not allowed", System.Net.HttpStatusCode.MethodNotAllowed);
            var contentType = request.Headers["Content-Type"] ?? string.Empty;
            if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) return ResourceHandler.ForErrorMessage("JSON required", System.Net.HttpStatusCode.UnsupportedMediaType);
            var elements = request.PostData?.Elements;
            if (elements == null || elements.Count != 1 || elements[0].Bytes == null) return ResourceHandler.ForErrorMessage("Body required", System.Net.HttpStatusCode.BadRequest);
            var bytes = elements[0].Bytes;
            if (bytes.Length > Protocol.MaxMessageBytes) return ResourceHandler.ForErrorMessage("Payload too large", System.Net.HttpStatusCode.RequestEntityTooLarge);
            _fetch(Encoding.UTF8.GetString(bytes));
            return ResourceHandler.FromString(" ", Encoding.UTF8, includePreamble: false, mimeType: "application/json");
        }

        private static string MimeType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": return "text/html";
                case ".js": return "text/javascript";
                case ".css": return "text/css";
                case ".json": case ".map": return "application/json";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                default: return "application/octet-stream";
            }
        }
    }
}
