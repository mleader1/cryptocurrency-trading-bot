using System;
using System.Collections.Generic;
using System.Linq;

namespace mleader.tradingbot.engines.Data
{
    public abstract class Orderbook
    {
        public abstract double Timestamp { get; set; }

        public abstract List<List<decimal>> Bids { get; set; }
        public abstract List<List<decimal>> Asks { get; set; }

        public decimal SellTotalInTargetCurrency => (Asks?.Sum(item => item[0] * item[1])).GetValueOrDefault();

        public abstract string Pair { get; set; }

        public abstract string Id { get; set; }

        public abstract decimal SellTotal { get; set; }

        public abstract decimal BuyTotal { get; set; }

        public abstract DateTime OrderTime { get; }
    }
}