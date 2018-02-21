using System;
using Newtonsoft.Json;

namespace mleader.tradingbot.Data.Cex
{
    public class FullOrder : IOrder
    {
        [JsonProperty("id")] public string OrderId { get; set; }


        public DateTime Timestamp => Time;

        [JsonProperty("time")] public DateTime Time { get; set; }

        [JsonProperty("type")] public OrderType Type { get; set; }

        [JsonProperty("price")] public decimal Price { get; set; }

        [JsonProperty("amount")] public decimal Amount { get; set; }

        [JsonProperty("pending")] public decimal Pending { get; set; }

        [JsonProperty("symbol1")] public string ExchangeCurrency { get; set; }

        [JsonProperty("symbol2")] public string TargetCurrency { get; set; }

        [JsonProperty("status")] public string Status { get; set; }
    }
}