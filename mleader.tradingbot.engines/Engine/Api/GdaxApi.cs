using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Cache;
using System.Net.Http;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using mleader.tradingbot.Data;
using mleader.tradingbot.engines.Data;
using mleader.tradingbot.engines.Data.Gdax;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
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
            var path = "/currencies";
            PrepareRequest(HttpMethod.Get, path);
            var currencyLimits = (await Rest.GetAsync<string>(path)).JsonDeserialize<List<GdaxCurrencyLimit>>();
            return (currencyLimits?.Count > 0)
                ? currencyLimits.Select(item => item as CurrencyLimit).ToList()
                : new List<CurrencyLimit>();
        }

        public async Task<TradingFees> GetAccountFeesAsync(string exchangeCurrency, string targetCurrency)
        {
            var path = "/users/self/trailing-volume";
            //get account trailing volume
            PrepareRequest(HttpMethod.Get, path);
            var volumes = (await Rest.GetAsync<string>(path)).JsonDeserialize<List<GdaxTrailingVolume>>();
            var exchangeVolume = volumes?.FirstOrDefault(item => item.ExchangeCurrency == exchangeCurrency);
            var fee = exchangeVolume?.GetTradingFee() ?? GdaxTradingFeeStructure.FeeLevels.FirstOrDefault();
            return new TradingFees
            {
                BuyingFeeInPercentage = fee.TakerFeeInPercent,
                SellingFeeInPercentage = fee.MakerFeeInPercent
            };
        }

        public async Task<AccountBalance> GetAccountBalanceAsync()
        {
            var path = "/accounts";
            PrepareRequest(HttpMethod.Get, path);
            var balance = (await Rest.GetAsync<string>(path)).JsonDeserialize<GdaxAccountBalance>();
            return balance?.ToAccountBalance();
        }

        public async Task<IOrder> ExecuteOrderAsync(OrderType orderType, string exchangeCurrency, string targetCurrency,
            decimal amount,
            decimal price)
        {
            var path = "/orders";
            var content = new
            {
                type = "limit",
                side = orderType == OrderType.Buy ? "buy" : "sell",
                product_id = $"{exchangeCurrency}-{targetCurrency}",
                price,
                size = amount,
                time_in_force = "GTC"
            };
            PrepareRequest(HttpMethod.Post, path, content.JsonSerialize());
            return (await Rest.PostAsync<string>(path, content)).JsonDeserialize<GdaxOrder>();
        }

        public async Task<bool> CancelOrderAsync(IOrder order)
        {
            if (order?.OrderId.IsNotNullOrEmpty() != true) return false;

            var path = $"/orders/{order?.OrderId}";
            PrepareRequest(HttpMethod.Delete, path);
            var result = await Rest.DeleteAsync<string>(path);
            return result.IsNullOrEmpty();
        }

        public async Task<List<ITradeHistory>> GetHistoricalTradeHistoryAsync(string exchangeCurrency,
            string targetCurrency,
            DateTime? from = null)
        {
            var path = $"/products/{exchangeCurrency}-{targetCurrency}/trades";
            PrepareRequest(HttpMethod.Get, path);
            var result = (await Rest.GetAsync<string>(path)).JsonDeserialize<List<GdaxTradeHistory>>();
            return result?.Select(item => item as ITradeHistory).ToList();
        }

        public async Task<Orderbook> GetPublicOrderbookAsync(string exchangeCurrency, string targetCurrency)
        {
            var path = $"/products/{exchangeCurrency}-{targetCurrency}/book";
            PrepareRequest(HttpMethod.Get, path);
            var result = (await Rest.GetAsync<string>(path)).JsonDeserialize<GdaxOrderbook>();
            if (result == null)
            {
                Console.WriteLine(await Rest.GetAsync<string>(path));
            }

            return result;
        }

        public async Task<List<IOrder>> GetAccountOpenOrdersAsync(string exchangeCurrency,
            string targetCurrency)
        {
            var path = $"/orders";
            var requestParams = new
            {
                product_id = $"{exchangeCurrency}-{targetCurrency}"
            };
            Rest.Add(requestParams);
            PrepareRequest(HttpMethod.Get, path, requestParams.JsonSerialize());
            var result = (await Rest.GetAsync<string>(path))
                .JsonDeserialize<List<GdaxOrder>>();
            return result?.Select(item => item as IOrder).ToList();
        }

        public async Task<List<IOrder>> GetAccountTradeHistoryAsync(string exchangeCurrency,
            string targetCurrency)
        {
            var path = $"/orders";
            var requestParams = new
            {
                product_id = $"{exchangeCurrency}-{targetCurrency}",
                status = "settled"
            };

            PrepareRequest(HttpMethod.Get, path, requestParams.JsonSerialize());
            Rest.Add(requestParams);
            var result =
                (await Rest.GetAsync<string>(path))
                .JsonDeserialize<List<GdaxOrder>>();
            return result?.Select(item => item as IOrder).ToList();
        }

        public void SendWebhookMessage(string message, string username)
        {
            try
            {
                if (SlackWebhook.IsNotNullOrEmpty() && message.IsNotNullOrEmpty())
                {
                    new Rest(SlackWebhook).PostAsync<string>("", new
                    {
                        text = message,
                        username = username.IsNullOrEmpty() ? $"MLEADER's Trading Bot [GDAX]" : username
                    }).Wait();
                }
            }
            catch (Exception ex)
            {
                Rest.LogDebug(ex.StackTrace, ex);
            }
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