using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using mleader.tradingbot.Data;
using mleader.tradingbot.engines.Data;
using mleader.tradingbot.engines.Data.Gdax;
using Microsoft.Extensions.Logging;
using OElite;

namespace mleader.tradingbot.Engine.Api
{
    public class GdaxApi : IApi
    {
        public Rest Rest { get; set; }
        public string SlackWebhook { get; set; }
        public ILogger Logger { get; set; }
        public ITradingStrategy TradingStrategy { get; set; }

        private string _apiKey;
        private string _apiSecret;
        private string _apiPassPhrase;


        public GdaxApi(string apiKey, string apiSecret, string apiPassPhrase, string slackWebhook, ILogger logger,
            ITradingStrategy tradingStrategy)
        {
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _apiPassPhrase = apiPassPhrase;
            SlackWebhook = slackWebhook;
            Logger = logger;
            TradingStrategy = tradingStrategy ?? new TradingStrategy
            {
                MinutesOfAccountHistoryOrderForPurchaseDecision = 60,
                MinutesOfAccountHistoryOrderForSellDecision = 60,
                MinutesOfPublicHistoryOrderForPurchaseDecision = 30,
                MinutesOfPublicHistoryOrderForSellDecision = 30,
                MinimumReservePercentageAfterInitInTargetCurrency = 0.1m,
                MinimumReservePercentageAfterInitInExchangeCurrency = 0.1m,
                OrderCapPercentageAfterInit = 0.6m,
                OrderCapPercentageOnInit = 0.25m,
                AutoDecisionExecution = true,
                MarketChangeSensitivityRatio = 0.01m,
                TradingSessionInHours = 24,
                TradingValueBleedRatio = 0.1m
            };
            Rest = new Rest("https://api.gdax.com",
                new RestConfig
                {
                    OperationMode = RestMode.HTTPRestClient,
                    UseRestConvertForCollectionSerialization = false
                },
                logger);
        }

        public async Task<List<CurrencyLimit>> GetCurrencyLimitsAsync()
        {
            PrepareRequest(HttpMethod.Get, "/currencies");
            var currencyLimits = await Rest.GetAsync<List<GdaxCurrencyLimit>>("/currencies");
            return (currencyLimits?.Count > 0)
                ? currencyLimits.Select(item => item as CurrencyLimit).ToList()
                : new List<CurrencyLimit>();
        }

        public Task<TradingFees> GetAccountFeesAsync(string exchangeCurrency, string targetCurrency)
        {
            throw new NotImplementedException();
        }

        public Task<AccountBalance> GetAccountBalanceAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IOrder> ExecuteOrderAsync(OrderType orderType, string exchangeCurrency, string targetCurrency,
            decimal amount,
            decimal price)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CancelOrderAsync(IOrder order)
        {
            throw new NotImplementedException();
        }

        public Task<List<ITradeHistory>> GetHistoricalTradeHistoryAsync(string exchangeCurrency, string targetCurrency,
            DateTime? @from = null)
        {
            throw new NotImplementedException();
        }

        public Task<Orderbook> GetPublicOrderbookAsync(string exchangeCurrency, string targetCurrency)
        {
            throw new NotImplementedException();
        }

        public Task<List<IOrder>> GetAccountOpenOrdersAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency)
        {
            throw new NotImplementedException();
        }

        public Task<List<IOrder>> GetAccountTradeHistoryAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency)
        {
            throw new NotImplementedException();
        }

        public void SendWebhookMessage(string message, string username)
        {
            throw new NotImplementedException();
        }

        private void PrepareRequest(HttpMethod httpMethod, string requestPath, string requestBody = "")
        {
            var timeStamp = DateTime.UtcNow.ToTimeStamp();
            var signedSignature = ComputeSignature(httpMethod, _apiSecret, timeStamp, requestPath, requestBody);
            AddHeaders(signedSignature, timeStamp);
        }

        private string ComputeSignature(HttpMethod httpMethod, string secret, double timestamp, string requestUri,
            string contentBody = "")
        {
            var convertedString = Convert.FromBase64String(secret);
            var prehash = timestamp.ToString("F0", CultureInfo.InvariantCulture) + httpMethod.ToString().ToUpper() +
                          requestUri + contentBody;
            return HashString(prehash, convertedString);
        }

        private string HashString(string str, byte[] secret)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            using (var hmaccsha = new HMACSHA256(secret))
            {
                return Convert.ToBase64String(hmaccsha.ComputeHash(bytes));
            }
        }

        private void AddHeaders(
            string signedSignature,
            double timeStamp)
        {
            Rest.AddHeader("User-Agent", "GDAXClient");
            Rest.AddHeader("CB-ACCESS-KEY", _apiKey);
            Rest.AddHeader("CB-ACCESS-TIMESTAMP", timeStamp.ToString("F0", CultureInfo.InvariantCulture));
            Rest.AddHeader("CB-ACCESS-SIGN", signedSignature);
            Rest.AddHeader("CB-ACCESS-PASSPHRASE", _apiPassPhrase);
        }
    }
}