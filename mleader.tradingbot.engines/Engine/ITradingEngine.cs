using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using mleader.tradingbot.Data;
using Microsoft.Extensions.Logging;

namespace mleader.tradingbot.Engine
{
    public interface ITradingEngine
    {
        IApi Api { get; set; }
        ITradingStrategy TradingStrategy { get; }

        decimal TradingStartBalanceInExchangeCurrency { get; set; }
        decimal TradingStartBalanceInTargetCurrency { get; set; }
        decimal TradingStartValueInExchangeCurrency { get; set; }
        decimal TradingStartValueInTargetCurrency { get; set; }


        string ReserveCurrency { get; set; }
        Dictionary<string, decimal> MinimumCurrencyOrderAmount { get; set; }

        List<ITradeHistory> LatestPublicSaleHistory { get; set; }
        List<ITradeHistory> LatestPublicPurchaseHistory { get; set; }
        List<IOrder> LatestAccountSaleHistory { get; set; }
        List<IOrder> LatestAccountPurchaseHistory { get; set; }


        List<IOrder> AccountOpenOrders { get; set; }
        IOrder AccountNextBuyOpenOrder { get; }
        IOrder AccountNextSellOpenOrder { get; }
        IOrder AccountLastBuyOpenOrder { get; }
        IOrder AccountLastSellOpenOrder { get; }

        DateTime TradingStartTime { get; set; }

        /// <summary>
        /// TODO: Used to implement re-value holding position as starting investment value (i.e. re-calculate the profitability based on each session start) 
        /// </summary>
        DateTime TradingSessionInHours { get; set; }

        /// <summary>
        /// TODO: Used to evaluate whether to execute orders when still above the stop line, but bleeding out (order executions that causes profit loss)
        /// If the order execution is within the profit loss using the BleedRatio based on TradingStartValueInExchangeCurrency or TradingStartValueInTargetCurrency
        /// </summary>
        decimal TradingValueBleedRatio { get; set; }

        DateTime LastTimeBuyOrderCancellation { get; set; }
        DateTime LastTimeSellOrderCancellation { get; set; }
        DateTime LastTimeBuyOrderExecution { get; set; }
        DateTime LastTimeSellOrderExecution { get; set; }
        DateTime LastCaculationTime { get; set; }

        Task<AccountBalance> GetAccountBalanceAsync();
        Task<List<IOrder>> GetOpenOrdersAsync();

        Task<bool> CancelOrderAsync(IOrder order);

        Task StartAsync();
        Task StopAsync();

        /// <summary>
        /// Load data required for strategy calculations from exchange Apis
        /// </summary>
        /// <returns></returns>
        Task<bool> MakeDecisionsAsync();

        /// <summary>
        /// [SellingPriceInPrinciple] = [ProposedSellingPrice] * (1+ [TradingFeeInPercentage] + [AverageTradingChangeRatio] * ([IsPublicUp]? 1: -1)) + [TradingFeeInAmount]
        /// </summary>
        /// <returns></returns>
        Task<decimal> GetSellingPriceInPrincipleAsync();

        /// <summary>
        /// [PurchasePriceInPrinciple] = [ProposedPurchasePrice] * (1 - [TradingFeeInPercentage] + [AverageTradingChangeRatio] * ([IsPublicUp] ? 1: -1)) + [TradingFeeInAmount]
        /// </summary>
        /// <returns></returns>
        Task<decimal> GetPurchasePriceInPrincipleAsync();
    }
}