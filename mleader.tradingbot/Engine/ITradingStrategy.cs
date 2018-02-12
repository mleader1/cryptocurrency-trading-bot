using System.Collections.Generic;
using mleader.tradingbot.Data;
using Microsoft.Extensions.Logging;

namespace mleader.tradingbot.Engine
{
    public interface ITradingStrategy
    {
        /// <summary>
        /// A percentage used to calculate Minimum reserved amount in reserved currency during the entire trading period
        /// the calculation is based on total investent portfolio valued based on exchange rate to reserve currency at each caculation time
        /// </summary>
        decimal MinimumReservePercentageAfterInit { get; set; }


        /// <summary>
        /// The maximum percentage for each order against total available amount in the pair trading when creating the first batch of orders in the trading process
        /// 
        /// NOTE: This program will initiate a batch of orders on start of the trading; and considered as "Init Orders", once after this, decisions will be made on per order basis  
        /// </summary>
        decimal OrderCapPercentageOnInit { get; set; }

        /// <summary>
        /// The maximum percentage for each order against total available amount in the pair trading after creating the first batch of orders in the trading process
        /// </summary>
        decimal OrderCapPercentageAfterInit { get; set; }


        /// <summary>
        /// When buying, history order transaction prices in each order on the exchange will be evaluated.
        /// Set this number for the engine to calculate the buying price 
        /// </summary>
        int MinutesOfPublicHistoryOrderForPurchaseDecision { get; set; }

        /// <summary>
        /// When buying, history order transaction prices in each purchase made in the past will be evaluated.
        /// Set this number for the engine to calculate the buying price 
        /// </summary>
        int MinutesOfAccountHistoryOrderForPurchaseDecision { get; set; }

        /// <summary>
        /// When selling, history order transaction prices in each order on the exchange will be evaluated.
        /// Set this number of the engine to calculate the selling price
        /// </summary>
        int MinutesOfPublicHistoryOrderForSellDecision { get; set; }

        /// <summary>
        /// When selling, history order transaction prices in each sale made in the past will be evaluated.
        /// Set this nuber for the engine to calculate the sellig price 
        /// </summary>
        int MinutesOfAccountHistoryOrderForSellDecision { get; set; }

        /// <summary>
        /// execute decisions (buy/sell) automatically without human intervention
        /// </summary>
        bool AutoDecisionExecution { get; set; }

        decimal StopLine { get; set; }

        /// <summary>
        /// Alert percentage of market price change that should be used for calculating buy/sell prices
        /// </summary>
        decimal MarketChangeSensitivityRatio { get; set; }
    }
}