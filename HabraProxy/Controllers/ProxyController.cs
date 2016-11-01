using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading.Tasks;
using System.Web.Hosting;
using System.Web.Mvc;
using AngleSharp.Dom;
using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using ExCSS;

namespace HabraProxy.Controllers
{
    public class ProxyController : Controller
    {
        private const string HABRA_URL = "https://habrahabr.ru";
        private static readonly char[] Punct = {'.', ',', ':', ';', '!', '?', '/', '\\'};
        private static readonly char[] QuotesAndPars = {'\'', '"', '«', '»', '(', ')', '[', ']'};

        public async Task<ActionResult> Index()
        {
            var url = Request.RawUrl;
            var client = new WebClient();
            var bytes = await client.DownloadDataTaskAsync($"{HABRA_URL}{url}");
            var content = Encoding.UTF8.GetString(bytes);

            if (string.IsNullOrWhiteSpace(content)) return Content(string.Empty);

            var parser = new HtmlParser();
            var document = parser.Parse(content);

            ProcessLinks(document);
            FixRelativeResourcesUrls(document);
            await DownloadFonts(document);
            LetsTm(document);

            return Content($"<!DOCTYPE html>{document.DocumentElement.OuterHtml}", "text/html", Encoding.UTF8);
        }

        private static void ProcessLinks(IHtmlDocument document)
        {
            var links = document.QuerySelectorAll($"body a[href*='{HABRA_URL}']");
            foreach (var link in links)
            {
                var oldHref = link.GetAttribute("href");
                // Make all links href relative, so they all will send browser to localhost
                var newHref = oldHref.Replace(HABRA_URL, "/").Replace("//", "/");

                link.SetAttribute("href", newHref);
            }
        }

        private static void FixRelativeResourcesUrls(IHtmlDocument document)
        {
            var links = document.QuerySelectorAll("head link");
            foreach (var link in links)
            {
                var href = link.GetAttribute("href");
                if (href.StartsWith("/"))
                {
                    href = $"{HABRA_URL}{href}";
                    link.SetAttribute("href", href);
                }
            }
        }

        private static async Task DownloadFonts(IHtmlDocument document)
        {
            var style = document.QuerySelector("head style");
            if (style == null) return;

            var urls = GetFontUrls(style.TextContent);

            foreach (var url in urls)
            {
                var clearUrl = url.Any(c => c == '?') ? url.Remove(url.IndexOf("?", StringComparison.Ordinal)) : url;
                var path = HostingEnvironment.MapPath(clearUrl);

                if (!System.IO.File.Exists(path))
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var webClient = new WebClient();
                    await webClient.DownloadFileTaskAsync($"{HABRA_URL}{clearUrl}", path);
                }
            }
        }

        private static void LetsTm(IHtmlDocument document)
        {
            var elements = document.QuerySelectorAll("head title, body *");
            foreach (var element in elements)
            {
                if (element.TagName == "SCRIPT") continue;

                foreach (var node in element.ChildNodes.Where(n => n.NodeType == NodeType.Text))
                {
                    var words = node.TextContent.Split(' ');
                    var processed = words.Select(CheckWord);

                    node.TextContent = string.Join(" ", processed);
                }
            }
        }

        private static string CheckWord(string word)
        {
            var cleared = word.Replace(" ", "").Replace("&nbsp;", "");

            if (cleared.Any(c => Punct.Any(p => p == c)))
            {
                var splitter = cleared.First(c => Array.IndexOf(Punct, c) != -1);
                var parts = cleared.Split(splitter);
                var tms = parts.Select(p => TryAddTm(p));
                var result = string.Join(splitter.ToString(), tms);

                return result;
            }

            return TryAddTm(cleared);
        }

        private static string TryAddTm(string word, string prefix = null, string suffix = null)
        {
            const string mark = "™";
            if (string.IsNullOrWhiteSpace(word)) return word;

            if (word.Length == 6 && !word.Any(c => QuotesAndPars.Any(p => p == c)))
                return $"{prefix}{word + mark}{suffix}";

            if (word.Length > 6 && word.Any(c => QuotesAndPars.Any(p => p == c)))
            {
                var curPrefix = QuotesAndPars.Any(c => c == word[0]) ? word[0] : (char?) null;
                var curSuffix = QuotesAndPars.Any(c => c == word[word.Length - 1])
                    ? word[word.Length - 1]
                    : (char?) null;

                if (curPrefix.HasValue) word = word.Replace(curPrefix.ToString(), "");
                if (curSuffix.HasValue) word = word.Replace(curSuffix.ToString(), "");

                if (curPrefix.HasValue || curSuffix.HasValue)
                    return TryAddTm(word, $"{prefix}{curPrefix}", $"{suffix}{curSuffix}");
            }

            return word;
        }

        private static IEnumerable<string> GetFontUrls(string cssString)
        {
            var parser = new Parser();
            var css = parser.Parse(cssString);

            var props =
                css.FontFaceDirectives.SelectMany(
                    f => f.Declarations.Where(d => d.Name.Equals("src", StringComparison.InvariantCultureIgnoreCase)))
                    .ToList();

            var urls = new List<string>();

            var tmp =
                props.Where(u => u.Term is PrimitiveTerm)
                    .Select(p => p.Term as PrimitiveTerm)
                    .Where(t => t.PrimitiveType == UnitType.Uri)
                    .Select(t => t.Value.ToString()).ToArray();

            urls.AddRange(tmp);

            tmp =
                props.Where(u => u.Term is TermList)
                    .Select(p => p.Term as TermList)
                    .SelectMany(t => t.OfType<PrimitiveTerm>())
                    .Where(t => t.PrimitiveType == UnitType.Uri)
                    .Select(t => t.Value.ToString()).ToArray();

            urls.AddRange(tmp);

            return urls;
        }
    }
}