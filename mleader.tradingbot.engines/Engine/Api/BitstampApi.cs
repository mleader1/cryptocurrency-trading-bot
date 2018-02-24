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
using mleader.tradingbot.engines.Data.Bitstamp;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OElite;
using System.Collections.Specialized;
using System.Dynamic;

namespace mleader.tradingbot.Engine.Api
{
    public class BitstampApi : IApi
    {
        public Rest Rest { get; set; }
        public string SlackWebhook { get; set; }
        public ILogger Logger { get; set; }
        public ITradingStrategy TradingStrategy { get; set; }

        public string ExchangeName => "Bitstamp";

        private string _apiKey;
        private string _apiSecret;
        private string _apiClientId;

        public BitstampApi(string apiKey, string apiSecret, string apiClientId, string slackWebhook, ILogger logger,
            ITradingStrategy tradingStrategy)
        {
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _apiClientId = apiClientId;
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
                PriceCorrectionFrequencyInHours = 24,
                TradingValueBleedRatio = 0.1m
            };
            Rest = new Rest("https://www.bitstamp.net/api/v2/",
                new RestConfig
                {
                    OperationMode = RestMode.HTTPClient,
                    UseRestConvertForCollectionSerialization = false
                },
                logger);
        }

        public async Task<List<CurrencyLimit>> GetCurrencyLimitsAsync()
        {
            return new List<CurrencyLimit>
            {
                new BitstampCurrencyLimit {ExchangeCurrency = "BTC", MinimumExchangeAmount = 0.001m},
                new BitstampCurrencyLimit {ExchangeCurrency = "USD", MinimumExchangeAmount = 5m},
                new BitstampCurrencyLimit {ExchangeCurrency = "GBP", MinimumExchangeAmount = 5m}
            };
        }

        public Task<TradingFees> GetAccountFeesAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency)
        {
            //TODO: Bitstamp does not have API for returning account fee based on trading volumes. A temporary trading fee structure needs to be created.
            return Task.FromResult(new TradingFees
            {
                SellingFeeInPercentage = 0.0025m,
                BuyingFeeInPercentage = 0.0025m
            });
        }

        public async Task<AccountBalance> GetAccountBalanceAsync()
        {
            var nonce = GetNonce();
            PrepareRest(new
            {
                key = _apiKey,
                signature = GetApiSignature(nonce),
                nonce
            });
            var balance = (await Rest.PostAsync<BitstampAccountBalance>("balance/"))?.ToAccountBalance();
            return balance;
        }

        public async Task<IOrder> ExecuteOrderAsync(OrderType orderType, string exchangeCurrency, string targetCurrency,
            decimal amount,
            decimal price)
        {
            var nonce = GetNonce();
            PrepareRest(new
            {
                signature = GetApiSignature(nonce),
                key = _apiKey,
                nonce,
                amount,
                price
            });

            var order = await Rest.PostAsync<BitstampOrder>(
                $"{(orderType == OrderType.Buy ? "buy" : "sell")}/{exchangeCurrency?.ToLower()}{targetCurrency?.ToLower()}/");
            return order;
        }

        public async Task<bool> CancelOrderAsync(IOrder order)
        {
            var nonce = GetNonce();
            PrepareRest(new
            {
                signature = GetApiSignature(nonce),
                key = _apiKey,
                nonce,
                id = order.OrderId
            });
            var result = await Rest.PostAsync<BitstampOrder>(
                "cancel_order/");

            return result?.OrderId == order.OrderId;
        }

        public async Task<List<ITradeHistory>> GetHistoricalTradeHistoryAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency, DateTime? from = null)
        {
            var latestThousandTradeHistories =
                (await Rest.GetAsync<string>(
                    $"transactions/{operatingExchangeCurrency?.ToLower()}{operatingTargetCurrency?.ToLower()}/"))
                .JsonDeserialize<List<BitstampTradehHistory>>();
            return
                latestThousandTradeHistories?.Where(item => item.Timestamp >= DateTime.UtcNow.AddMinutes(-1 * (
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
                await Rest.GetAsync<BitstampOrderbook>(
                    $"order_book/{exchangeCurrency?.ToLower()}{targetCurrency?.ToLower()}/");
            return orderBook;
        }

        public async Task<List<IOrder>> GetAccountOpenOrdersAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency)
        {
            var nonce = GetNonce();
            PrepareRest(new
            {
                signature = GetApiSignature(nonce),
                key = _apiKey,
                nonce
            });
            var orders = (await Rest.PostAsync<string>(
                    $"open_orders/{operatingExchangeCurrency?.ToLower()}{operatingTargetCurrency?.ToLower()}/"))
                .JsonDeserialize<List<BitstampOrder>>();
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
            PrepareRest(new
            {
                key = _apiKey,
                signature = GetApiSignature(nonce),
                nonce
            });
            var latestAccountTradeHistories = (await Rest.PostAsync<string>(
                    $"user_transactions/{operatingExchangeCurrency?.ToLower()}{operatingTargetCurrency?.ToLower()}/"))
                .JsonDeserialize<List<BitstampUserTransaction>>();
            return latestAccountTradeHistories?.Where(item => item.Status == "d").Select(item => item as IOrder)
                .ToList();
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
                        username = username.IsNullOrEmpty() ? $"MLEADER's Trading Bot [Bitstamp]" : username
                    }).Wait();
                }
            }
            catch (Exception ex)
            {
                Rest.LogDebug(ex.StackTrace, ex);
            }
        }

        private void PrepareRest(object values)
        {
            var v = values?.JsonSerialize()?.JsonDeserialize<Dictionary<string, string>>();
            v?.ToList()?.ForEach(item => Rest.Add(item.Key, item.Value));
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
            var hashInput = string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}", nonce, _apiClientId,
                _apiKey);
            var hashInputBytes = Encoding.ASCII.GetBytes(hashInput);

            var secretBytes = Encoding.ASCII.GetBytes(_apiSecret);
            var hmac = new HMACSHA256(secretBytes);
            var signatureBytes = hmac.ComputeHash(hashInputBytes);
            var signature = BitConverter.ToString(signatureBytes).Replace("-", "").ToUpper();
            return signature;
        }
    }
}