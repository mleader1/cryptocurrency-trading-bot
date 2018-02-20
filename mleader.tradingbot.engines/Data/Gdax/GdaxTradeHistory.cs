using System;
using mleader.tradingbot.Data;
using Newtonsoft.Json;
using OElite;

namespace mleader.tradingbot.engines.Data.Gdax
{
    public class GdaxTradeHistory : ITradeHistory
    {
        [JsonProperty("trade_id")] public string Id { get; set; }
        [JsonProperty("side")] public string Side { get; set; }

        public OrderType OrderType
        {
            get => Side == "buy" ? OrderType.Buy : OrderType.Sell;
            set { }
        }

        [JsonProperty("size")] public decimal Amount { get; set; }
        [JsonProperty("price")] public decimal Price { get; set; }
        public DateTime Timestamp => Time.JsonDeserialize<DateTime>();

        [JsonProperty("time")] public string Time { get; set; }
    }
}