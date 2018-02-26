using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using mleader.tradingbot.Data;
using mleader.tradingbot.Data.Cex;
using mleader.tradingbot.engines.Data;
using Microsoft.Extensions.Logging;
using OElite;

namespace mleader.tradingbot.Engine
{
    public interface IApi
    {
        Rest Rest { get; set; }
        string SlackWebhook { get; set; }
        ILogger Logger { get; set; }
        ITradingStrategy TradingStrategy { get; set; }
        string ExchangeName { get; }

        Task<List<CurrencyLimit>> GetCurrencyLimitsAsync();
        Task<TradingFees> GetAccountFeesAsync(string exchangeCurrency, string targetCurrency);

        Task<AccountBalance> GetAccountBalanceAsync();

        Task<IOrder> ExecuteOrderAsync(OrderType orderType, string exchangeCurrency, string targetCurrency,
            decimal amount, decimal price);

        Task<bool> CancelOrderAsync(IOrder order);


        Task<List<ITradeHistory>> GetHistoricalTradeHistoryAsync(string exchangeCurrency, string targetCurrency,
            DateTime? from = null);

        Task<Orderbook> GetPublicOrderbookAsync(string exchangeCurrency, string targetCurrency);


        Task<List<IOrder>> GetAccountOpenOrdersAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency);

        Task<List<IOrder>> GetAccountTradeHistoryAsync(string operatingExchangeCurrency,
            string operatingTargetCurrency);


        void SendWebhookMessage(string message, string username);
    }
}