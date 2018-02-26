using System;
using System.Runtime.Serialization;
using mleader.tradingbot.Data;
using Newtonsoft.Json;

namespace mleader.tradingbot.engines.Data.Bitstamp
{
    public class BitstampOrder : IOrder
    {
        [JsonProperty("id")] public string OrderId { get; set; }


        [JsonProperty("datetime")] public DateTime Timestamp { get; set; }

        [JsonProperty("type")] public string BitstampOrderType { get; set; }

        [JsonIgnore]
        [IgnoreDataMember] public OrderType Type
        {
            get => BitstampOrderType == "0" ? OrderType.Buy : OrderType.Sell;
            set { }
        }


        [JsonProperty("price")] public decimal Price { get; set; }

        [JsonProperty("amount")] public decimal Amount { get; set; }

        public decimal Pending { get; set; }

        public string ExchangeCurrency { get; set; }

        public string TargetCurrency { get; set; }
    }
}