using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using EasyAbp.Abp.WeChat.Official.Infrastructure.Models;
using Volo.Abp.DependencyInjection;

namespace EasyAbp.Abp.WeChat.Official.Infrastructure
{
    [Dependency(TryRegister = true)]
    public class DefaultWeChatOfficialApiRequester : IWeChatOfficialApiRequester, ITransientDependency
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IAccessTokenAccessor _accessTokenAccessor;

        public DefaultWeChatOfficialApiRequester(IHttpClientFactory httpClientFactory,
            IAccessTokenAccessor accessTokenAccessor)
        {
            _httpClientFactory = httpClientFactory;
            _accessTokenAccessor = accessTokenAccessor;
        }

        #region > Public Methods <

        public virtual async Task<string> RequestAsync(string targetUrl,
            HttpMethod method,
            IOfficialRequest officialRequest = null,
            bool withAccessToken = true)
        {
            var client = _httpClientFactory.CreateClient();

            targetUrl = targetUrl.EnsureEndsWith('?');

            if (withAccessToken)
            {
                targetUrl += $"access_token={await _accessTokenAccessor.GetAccessTokenAsync()}";
            }

            var requestMsg = method == HttpMethod.Get
                ? BuildHttpGetRequestMessage(targetUrl, officialRequest)
                : BuildHttpPostRequestMessage(targetUrl, officialRequest);

            return await (await client.SendAsync(requestMsg)).Content.ReadAsStringAsync();
        }

        public virtual async Task<TResponse> RequestAsync<TResponse>(string targetUrl,
            HttpMethod method,
            IOfficialRequest officialRequest = null,
            bool withAccessToken = true)
        {
            var resultStr = await RequestAsync(targetUrl,
                method,
                officialRequest,
                withAccessToken);

            return JsonConvert.DeserializeObject<TResponse>(resultStr);
        }

        public virtual async Task<TResponse> RequestFromDataAsync<TResponse>(string targetUrl,
            MultipartFormDataContent formDataContent,
            IOfficialRequest officialRequest = null,
            bool withAccessToken = true)
        {
            var client = _httpClientFactory.CreateClient();
            targetUrl = targetUrl.EnsureEndsWith('?');

            if (withAccessToken)
            {
                targetUrl += $"access_token={await _accessTokenAccessor.GetAccessTokenAsync()}";
            }

            var requestMsg = BuildHttpGetRequestMessage(targetUrl, officialRequest);
            requestMsg.Method = HttpMethod.Post;

            // 处理 HttpClient 提交表单的问题。
            // 链接: https://www.cnblogs.com/myzony/p/12114507.html
            var boundaryValue = formDataContent.Headers.ContentType.Parameters.Single(p => p.Name == "boundary");
            boundaryValue.Value = boundaryValue.Value.Replace("\"", String.Empty);
            requestMsg.Content = formDataContent;

            var responseString = await (await client.SendAsync(requestMsg)).Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<TResponse>(responseString);
        }

        #endregion

        #region > Private Methods <

        private HttpRequestMessage BuildHttpGetRequestMessage(string targetUrl, IOfficialRequest officialRequest)
        {
            if (officialRequest == null)
            {
                return new HttpRequestMessage(HttpMethod.Get, targetUrl);
            }

            var requestUrl = BuildQueryString(targetUrl, officialRequest);
            return new HttpRequestMessage(HttpMethod.Get, requestUrl);
        }

        private HttpRequestMessage BuildHttpPostRequestMessage(string targetUrl, IOfficialRequest officialRequest)
        {
            return new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = new StringContent(officialRequest.ToString())
            };
        }

        private string BuildQueryString(string targetUrl, IOfficialRequest request)
        {
            if (request == null)
            {
                return targetUrl;
            }

            var type = request.GetType();
            var properties = type.GetProperties();

            if (properties.Length > 0)
            {
                targetUrl = targetUrl.EnsureEndsWith('&');
            }

            var queryStringBuilder = new StringBuilder(targetUrl);

            foreach (var propertyInfo in properties)
            {
                var jsonProperty = propertyInfo.GetCustomAttribute<JsonPropertyAttribute>();
                var propertyName = jsonProperty != null ? jsonProperty.PropertyName : propertyInfo.Name;

                queryStringBuilder.Append($"{propertyName}={propertyInfo.GetValue(request)}&");
            }

            return queryStringBuilder.ToString().TrimEnd('&');
        }

        #endregion
    }
}