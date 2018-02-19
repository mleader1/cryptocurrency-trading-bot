using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using mleader.tradingbot.engines.Data;
using Newtonsoft.Json;

namespace mleader.tradingbot.Data.Cex
{
    public class CexOrderbook : Orderbook
    {
        [JsonProperty("timestamp")]
        public override double Timestamp { get; set; }

        [JsonProperty("bids")]
        public override List<List<decimal>> Bids { get; set; }

        [JsonProperty("asks")]
        public override List<List<decimal>> Asks { get; set; }

        [JsonProperty("pair")]
        public override string Pair { get; set; }

        [JsonProperty("id")]
        public override string Id { get; set; }

        [JsonProperty("sell_total")]
        public override decimal SellTotal { get; set; }

        [JsonProperty("buy_total")]
        public override decimal BuyTotal { get; set; }

        public override DateTime OrderTime
        {
            get
            {
                try
                {
                    var convertDateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    convertDateTime = convertDateTime.AddMilliseconds(Timestamp).ToLocalTime();
                    return convertDateTime;
                }
                catch (Exception ex)
                {
                }

                return default(DateTime);
            }
        }
    }
}