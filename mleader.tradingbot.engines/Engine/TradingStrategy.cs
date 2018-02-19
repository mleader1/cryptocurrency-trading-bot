using System;
using System.Collections.Generic;
using System.Linq;
using mleader.tradingbot.Data;
using Microsoft.Extensions.Logging;
using OElite;
using OElite.Utils;

namespace mleader.tradingbot.Engine
{
    public class TradingStrategy : ITradingStrategy
    {
        public decimal MinimumReservePercentageAfterInitInTargetCurrency { get; set; }
        public decimal MinimumReservePercentageAfterInitInExchangeCurrency { get; set; }
        public decimal OrderCapPercentageOnInit { get; set; }
        public decimal OrderCapPercentageAfterInit { get; set; }
        public int MinutesOfPublicHistoryOrderForPurchaseDecision { get; set; }
        public int MinutesOfAccountHistoryOrderForPurchaseDecision { get; set; }
        public int MinutesOfPublicHistoryOrderForSellDecision { get; set; }
        public int MinutesOfAccountHistoryOrderForSellDecision { get; set; }
        public bool AutoDecisionExecution { get; set; }
        public decimal StopLine { get; set; }
        public decimal MarketChangeSensitivityRatio { get; set; }
        public decimal TradingSessionInHours { get; set; }
        public decimal TradingValueBleedRatio { get; set; }

    }
}