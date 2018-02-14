using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Newtonsoft.Json;

namespace mleader.tradingbot.Data.Cex
{
    public class Orderbook
    {
        [JsonProperty("timestamp")]
        public double Timestamp { get; set; }

        [JsonProperty("bids")]
        public List<List<decimal>> Bids { get; set; }

        [JsonProperty("asks")]
        public List<List<decimal>> Asks { get; set; }

        public decimal SellTotalInTargetCurrency => (Asks?.Sum(item => item[0] * item[1])).GetValueOrDefault();

        [JsonProperty("pair")]
        public string Pair { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("sell_total")]
        public decimal SellTotal { get; set; }

        [JsonProperty("buy_total")]
        public decimal BuyTotal { get; set; }

        public DateTime OrderTime
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