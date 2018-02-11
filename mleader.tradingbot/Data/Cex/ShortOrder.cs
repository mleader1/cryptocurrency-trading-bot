using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace mleader.tradingbot.Data.Cex
{
    public class ShortOrder : IOrder
    {
        [JsonProperty("id")]
        public string OrderId { get; set; }


        public DateTime Timestamp
        {
            get
            {
                var convertDateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                convertDateTime = convertDateTime.AddSeconds(Time).ToLocalTime();
                return convertDateTime;
            }
        }

        [JsonProperty("time")]
        public double Time { get; set; }

        [JsonProperty("type")]
        public OrderType Type { get; set; }

        [JsonProperty("price")]
        public decimal Price { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("pending")]
        public decimal Pending { get; set; }

        [JsonProperty("symbol1")]
        public string ExchangeCurrency { get; set; }

        [JsonProperty("symbol2")]
        public string TargetCurrency { get; set; }
    }
}