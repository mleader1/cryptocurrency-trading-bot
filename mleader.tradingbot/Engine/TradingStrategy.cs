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
        public decimal MinimumReservePercentageAfterInit { get; set; }
        public decimal OrderCapPercentageOnInit { get; set; }
        public decimal OrderCapPercentageAfterInit { get; set; }
        public int HoursOfPublicHistoryOrderForPurchaseDecision { get; set; }
        public int HoursOfAccountHistoryOrderForPurchaseDecision { get; set; }
        public int HoursOfPublicHistoryOrderForSellDecision { get; set; }
        public int HoursOfAccountHistoryOrderForSellDecision { get; set; }
        public bool AutoDecisionExecution { get; set; }
        public decimal StopLine { get; set; }
        public decimal MarketChangeSensitivityRatio { get; set; }
    }
}