using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using EasyAbp.Abp.WeChat.Common.Infrastructure.Signature;
using EasyAbp.Abp.WeChat.Pay.Exceptions;
using EasyAbp.Abp.WeChat.Pay.Infrastructure;
using EasyAbp.Abp.WeChat.Pay.Infrastructure.OptionResolve;
using EasyAbp.Abp.WeChat.Pay.Models;

namespace EasyAbp.Abp.WeChat.Pay.Services
{
    /// <summary>
    /// 微信支付服务的基类定义，主要提供了常用组件的组入，例如签名生成组件等。
    /// </summary>
    public abstract class WeChatPayService : ITransientDependency
    {
        public IServiceProvider ServiceProvider { get; set; }

        protected readonly object ServiceLocker = new object();

        protected TService LazyLoadService<TService>(ref TService service)
        {
            if (service == null)
            {
                lock (ServiceLocker)
                {
                    if (service == null)
                    {
                        service = ServiceProvider.GetRequiredService<TService>();
                    }
                }
            }

            return service;
        }

        protected ISignatureGenerator SignatureGenerator => LazyLoadService(ref _signatureGenerator);
        private ISignatureGenerator _signatureGenerator;

        protected IWeChatPayApiRequester WeChatPayApiRequester => LazyLoadService(ref _weChatPayApiRequester);
        private IWeChatPayApiRequester _weChatPayApiRequester;

        public ILoggerFactory LoggerFactory => LazyLoadService(ref _loggerFactory);
        private ILoggerFactory _loggerFactory;

        private Lazy<ILogger> _lazyLogger => new Lazy<ILogger>(() => LoggerFactory?.CreateLogger(GetType().FullName) ?? NullLogger.Instance, true);
        protected ILogger Logger => _lazyLogger.Value;

        protected IWeChatPayOptionsResolver AbpWeChatPayOptionsResolver => LazyLoadService(ref _weChatPayOptionsResolver);
        private IWeChatPayOptionsResolver _weChatPayOptionsResolver;

        protected IHttpClientFactory HttpClientFactory => LazyLoadService(ref _httpClientFactory);
        private IHttpClientFactory _httpClientFactory;

        protected virtual async Task<XmlDocument> RequestAndGetReturnValueAsync(string targetUrl, WeChatPayParameters requestParameters)
        {
            var result = await WeChatPayApiRequester.RequestAsync(targetUrl, requestParameters.ToXmlStr());
            if (result.SelectSingleNode("/xml/return_code")?.InnerText != "SUCCESS" ||
                result.SelectSingleNode("/xml/return_msg")?.InnerText != "OK")
            {
                var errMsg = $"微信支付接口调用失败，具体失败原因：{result.SelectSingleNode("/xml/err_code_des")?.InnerText ?? result.SelectSingleNode("/xml/return_msg")?.InnerText}";
                Logger.Log(LogLevel.Error, errMsg, targetUrl, requestParameters);

                var exception = new CallWeChatPayApiException(errMsg);
                exception.Data.Add(nameof(targetUrl), targetUrl);
                exception.Data.Add(nameof(requestParameters), requestParameters);

                throw exception;
            }

            return result;
        }

        protected virtual Task<IWeChatPayOptions> GetAbpWeChatPayOptions() => AbpWeChatPayOptionsResolver.ResolveAsync();

        protected virtual async ValueTask<bool> CurrentIsSandboxModeAsync()
        {
            return (await GetAbpWeChatPayOptions()).IsSandBox;
        }

        protected virtual async ValueTask<string> GetRequestUrl(string standardUrl)
        {
            if (await CurrentIsSandboxModeAsync())
            {
                // Todo: 原仿真测试系统已不再维护，下线时间2022年5月31日，如有问题请通过新版文档中心技术咨询进行反馈。请需要进行支付测试的商户按照下面新的仿真测试系统说明文档进行接入测试 https://pay.weixin.qq.com/wiki/doc/api/micropay.php?chapter=23_1&index=1
                return Regex.Replace(standardUrl, "/pay/", "/sandboxnew/pay/");
            }

            return standardUrl;
        }
    }
}