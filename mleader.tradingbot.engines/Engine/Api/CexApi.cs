using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using mleader.tradingbot.Data;
using mleader.tradingbot.Data.Cex;
using mleader.tradingbot.engines.Data;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OElite;
using StackExchange.Redis;

namespace mleader.tradingbot.Engine.Api
{
    public class CexApi : IApi
    {
        private string _apiKey;
        private string _apiSecret;
        private string _apiUsername;

        public CexApi(string apiKey, string apiSecret, string apiUsername, string slackWebhook, ILogger logger,
            TradingStrategy tradingStrategy)
        {
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _apiUsername = apiUsername;
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
            Rest = new Rest("https://cex.io/api/",
                new RestConfig
                {
                    OperationMode = RestMode.HTTPRestClient,
                    UseRestConvertForCollectionSerialization = false
                },
                logger);
        }


        public Rest Rest { get; set; }
        public string SlackWebhook { get; set; }
        public ILogger Logger { get; set; }
        public ITradingStrategy TradingStrategy { get; set; }
        public int ApiRequestCounts { get; set; }

        public async Task<List<CurrencyLimit>> GetCurrencyLimitsAsync()
        {
            var result = await Rest.GetAsync<JObject>("currency_limits");
            var limits = result?["data"]?["pairs"]?.Children()
                .Select(item => item.ToObject<CexCurrencyLimit>() as CurrencyLimit);
            ApiRequestCounts++;
            return limits?.ToList() ?? new List<CurrencyLimit>();
        }

        public async Task<TradingFees> GetAccountFeesAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency)
        {
            var nonce = GetNonce();
            var myFees = await Rest.PostAsync<JObject>("get_myfee", new
            {
                key = _apiKey,
                signature = GetApiSignature(nonce),
                nonce
            });
            ApiRequestCounts++;
            var fee = new TradingFees
            {
                SellingFeeInPercentage = (myFees?.GetValue("data")
                                             ?.Value<JToken>($"{operatingExchangeCurrency}:{operatingTargetCurrency}")
                                             ?.Value<decimal>("sell"))
                                         .GetValueOrDefault() / 100,
                SellingFeeInAmount = 0,
                BuyingFeeInPercentage = (myFees?.GetValue("data")
                                            ?.Value<JToken>($"{operatingExchangeCurrency}:{operatingTargetCurrency}")
                                            ?.Value<decimal>("buy"))
                                        .GetValueOrDefault() / 100,
                BuyingFeeInAmount = 0
            };
            return fee;
        }

        public async Task<AccountBalance> GetAccountBalanceAsync()
        {
            var nonce = GetNonce();
            var balance = (await Rest.PostAsync<CexAccountBalance>("balance/", new
            {
                key = _apiKey,
                signature = GetApiSignature(nonce),
                nonce
            }))?.ToAccountBalance();
            ApiRequestCounts++;
            return balance;
        }

        public async Task<IOrder> ExecuteOrderAsync(OrderType orderType, string exchangeCurrency, string targetCurrency,
            decimal amount,
            decimal price)
        {
            var nonce = GetNonce();
            var order = await Rest.PostAsync<ShortOrder>(
                $"place_order/{exchangeCurrency}/{targetCurrency}", new
                {
                    signature = GetApiSignature(nonce),
                    key = _apiKey,
                    nonce,
                    type = orderType == OrderType.Buy ? "buy" : "sell",
                    amount,
                    price
                });
            ApiRequestCounts++;

            if (order != null) return order;

            nonce = GetNonce();
            var error = await Rest.PostAsync<string>(
                $"place_order/{exchangeCurrency}/{targetCurrency}", new
                {
                    signature = GetApiSignature(nonce),
                    key = _apiKey,
                    nonce,
                    type = orderType == OrderType.Buy ? "buy" : "sell",
                    amount,
                    price
                });
            ApiRequestCounts++;

            Logger.LogError(
                $" [FAILED] BUY Order FAILED: {amount} {exchangeCurrency} at {price} per {exchangeCurrency} \n{error}");

            return null;
        }

        public async Task<bool> CancelOrderAsync(IOrder order)
        {
            var nonce = GetNonce();
            var result = await Rest.PostAsync<string>(
                $"cancel_order/", new
                {
                    signature = GetApiSignature(nonce),
                    key = _apiKey,
                    nonce,
                    id = order.OrderId
                });
            ApiRequestCounts++;
            return BooleanUtils.GetBooleanValueFromObject(result);
        }

        public async Task<List<ITradeHistory>> GetHistoricalTradeHistoryAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency, DateTime? from = null)
        {
            var latestThousandTradeHistories =
                await Rest.GetAsync<List<CexTradeHistory>>(
                    $"trade_history/{operatingExchangeCurrency}/{operatingTargetCurrency}");
            ApiRequestCounts++;
            return
                latestThousandTradeHistories.Where(item => item.Timestamp >= DateTime.UtcNow.AddMinutes(-1 * (
                                                                                                            TradingStrategy
                                                                                                                .MinutesOfPublicHistoryOrderForPurchaseDecision >
                                                                                                            TradingStrategy
                                                                                                                .MinutesOfPublicHistoryOrderForSellDecision
                                                                                                                ? TradingStrategy
                                                                                                                    .MinutesOfPublicHistoryOrderForPurchaseDecision
                                                                                                                : TradingStrategy
                                                                                                                    .MinutesOfPublicHistoryOrderForSellDecision
                                                                                                        ))).Select(
                    item => item as ITradeHistory).ToList() ?? new List<ITradeHistory>();
        }

        public async Task<Orderbook> GetPublicOrderbookAsync(string exchangeCurrency, string targetCurrency)
        {
            var orderBook =
                await Rest.GetAsync<CexOrderbook>($"order_book/{exchangeCurrency}/{targetCurrency}/");
            ApiRequestCounts++;
            return orderBook;
        }

        public async Task<List<IOrder>> GetAccountOpenOrdersAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency)
        {
            var nonce = GetNonce();
            var orders = await Rest.PostAsync<List<ShortOrder>>(
                $"open_orders/{operatingExchangeCurrency}/{operatingTargetCurrency}", new
                {
                    signature = GetApiSignature(nonce),
                    key = _apiKey,
                    nonce
                });
            ApiRequestCounts++;
            orders?.ForEach(item =>
            {
                item.TargetCurrency = operatingTargetCurrency;
                item.ExchangeCurrency = operatingExchangeCurrency;
            });
            var accountOpenOrders =
                orders?.Where(item =>
                    item.ExchangeCurrency == operatingExchangeCurrency &&
                    item.TargetCurrency == operatingTargetCurrency).Select(item => item as IOrder).ToList() ??
                new List<IOrder>();
            return accountOpenOrders;
        }

        public async Task<List<IOrder>> GetAccountTradeHistoryAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency)
        {
            var nonce = GetNonce();
            var latestAccountTradeHistories = await Rest.PostAsync<List<FullOrder>>(
                $"archived_orders/{operatingExchangeCurrency}/{operatingTargetCurrency}", new
                {
                    Key = _apiKey,
                    Signature = GetApiSignature(nonce),
                    Nonce = nonce,
                    DateFrom = (DateTime.UtcNow.AddMinutes(
                                    -1 * (TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision >
                                          TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision
                                        ? TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision
                                        : TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision)) -
                                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
                    DateTo = (DateTime.UtcNow.AddMinutes(
                                  (TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision >
                                   TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision
                                      ? TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision
                                      : TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision)) -
                              new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
                });
            ApiRequestCounts++;
            return latestAccountTradeHistories?.Select(item => item as IOrder).ToList();
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
                        username = username.IsNullOrEmpty() ? $"MLEADER's Trading Bot [CEX.IO]" : username
                    }).Wait();
                }
            }
            catch (Exception ex)
            {
                Rest.LogDebug(ex.StackTrace, ex);
            }
        }


        private long GetNonce()
        {
            return Convert.ToInt64(Math.Truncate((DateTime.UtcNow - DateTime.MinValue).TotalMilliseconds));
        }

        private string GetApiSignature(long nonce)
        {
            // Validation first
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new ArgumentException("Parameter apiUsername is not set.");
            }

            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new ArgumentException("Parameter apiKey is not set");
            }

            if (string.IsNullOrEmpty(_apiSecret))
            {
                throw new ArgumentException("Parameter apiSecret is not set");
            }

            // HMAC input is nonce + username + key
            var hashInput = string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}", nonce, _apiUsername,
                _apiKey);
            var hashInputBytes = Encoding.UTF8.GetBytes(hashInput);

            var secretBytes = Encoding.UTF8.GetBytes(_apiSecret);
            var hmac = new HMACSHA256(secretBytes);
            var signatureBytes = hmac.ComputeHash(hashInputBytes);
            var signature = BitConverter.ToString(signatureBytes).ToUpper().Replace("-", string.Empty);
            return signature;
        }
    }
}