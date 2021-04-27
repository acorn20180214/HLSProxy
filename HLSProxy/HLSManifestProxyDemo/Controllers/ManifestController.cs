using Microsoft.Azure;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Http;

namespace HLSManifestProxyDemo.Controllers
{
    public class ManifestController : ApiController
    {
        /// <summary>
        /// GET /AzureMediaservicesManifestProxy/TopLevel(playbackUrl,token)
        /// </summary>
        [Route("TopLevel")]
        public HttpResponseMessage GetTopLevelHslPlaylist(string playbackUrl, string token)
        {
            if (playbackUrl.ToLowerInvariant().EndsWith("manifest"))
            {
                playbackUrl += "(format=m3u8-aapl)";
            }

            string secondLevelProxyUrl = GetBaseUrl() + "/SecondLevel";

            var modifiedTopLeveLManifest = GetTopLevelManifestForToken(playbackUrl, token, secondLevelProxyUrl);

            var response = this.Request.CreateResponse();

            response.Content = new StringContent(modifiedTopLeveLManifest, Encoding.UTF8, "application/vnd.apple.mpegurl");

            response.Headers.Add("X-Content-Type-Options", "nosniff");
            response.Headers.Add("Cache-Control", "max-age=259200");

            //_log.Info($"TopLevelResult: {modifiedTopLeveLManifest}");

            return response;
        }

        /// <summary>
        /// GET /AzureMediaservicesManifestProxy/SecondLevel(playbackUrl,token)
        /// </summary>
        [Route("SecondLevel")]
        public HttpResponseMessage GetSecondLevelHslPlaylist(string playbackUrl, string token)
        {
            // get rid of "Bearer=" or "Bearer " prefixes
            if (token.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring("Bearer".Length + 1).Trim();
            }

            string encodedToken = HttpUtility.UrlEncode(token);

            const string qualityLevelRegex = @"(QualityLevels\(\d+\))";
            const string fragmentsRegex = @"(Fragments\([\w\d=-]+,[\w\d=-]+\))";
            const string urlRegex = @"("")(https?:\/\/[\da-z\.-]+\.[a-z\.]{2,6}[\/\w \.-]*\/?[\?&][^&=]+=[^&=#]*)("")";

            string baseUrl = playbackUrl.Substring(0, playbackUrl.IndexOf(".ism", System.StringComparison.OrdinalIgnoreCase)) + ".ism";
            string content = GetRawContent(playbackUrl);

            string newContent = Regex.Replace(content, urlRegex, string.Format(CultureInfo.InvariantCulture, "$1$2&token={0}$3", encodedToken));
            Match match = Regex.Match(playbackUrl, qualityLevelRegex);
            if (match.Success)
            {
                var qualityLevel = match.Groups[0].Value;
                newContent = Regex.Replace(newContent, fragmentsRegex, m => string.Format(CultureInfo.InvariantCulture, baseUrl + "/" + qualityLevel + "/" + m.Value));
            }

            HttpResponseMessage response = this.Request.CreateResponse();

            response.Content = new StringContent(newContent, Encoding.UTF8, "application/vnd.apple.mpegurl");
            CloudConfigurationManager.GetSetting("");
            //_log.Info($"SecondLevelResult: {newContent}");

            return response;
        }

        private string GetTopLevelManifestForToken(string topLeveLManifestUrl, string token, string secondLevelManifestProxyBaseUrl)
        {
            const string qualityLevelRegex = @"(QualityLevels\(\d+\)/Manifest\(.+\))";

            string topLevelManifestContent = GetRawContent(topLeveLManifestUrl);

            string topLevelManifestBaseUrl = topLeveLManifestUrl.Substring(0, topLeveLManifestUrl.IndexOf(".ism", System.StringComparison.OrdinalIgnoreCase)) + ".ism";
            string urlEncodedTopLeveLManifestBaseUrl = HttpUtility.UrlEncode(topLevelManifestBaseUrl);
            string urlEncodedToken = HttpUtility.UrlEncode(token);

            MatchEvaluator encodingReplacer = (Match m) => $"{secondLevelManifestProxyBaseUrl}?playbackUrl={urlEncodedTopLeveLManifestBaseUrl}{HttpUtility.UrlEncode("/" + m.Value)}&token={urlEncodedToken}";

            string newContent = Regex.Replace(topLevelManifestContent, qualityLevelRegex, encodingReplacer);

            return newContent;
        }

        private string GetBaseUrl()
        {
            Uri requestUri = Request.RequestUri;
            string baseUrl = Request.RequestUri.GetLeftPart(UriPartial.Authority);
            return baseUrl;
        }

        private string GetRawContent(string uri)
        {
            var httpRequest = (HttpWebRequest)WebRequest.Create(new Uri(uri));
            httpRequest.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);
            httpRequest.Timeout = 30000;

            using (WebResponse httpResponse = httpRequest.GetResponse())
            using (Stream stream = httpResponse.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}