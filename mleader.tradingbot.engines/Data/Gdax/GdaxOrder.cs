using System;
using System.Data.Common;
using System.Linq;
using System.Runtime.Serialization;
using mleader.tradingbot.Data;
using Newtonsoft.Json;

namespace mleader.tradingbot.engines.Data.Gdax
{
    public class GdaxOrder : IOrder
    {
        [DataMember(Name = "Id")]
        [JsonProperty("id")]
        public string OrderId { get; set; }

        [DataMember(Name = "created_at")]
        [JsonProperty("created_at")]
        public DateTime Timestamp { get; }

        [DataMember(Name = "side")]
        [JsonProperty("side")]
        public string OrderSide { get; set; }

        public OrderType Type
        {
            get => OrderSide == "buy" ? OrderType.Buy : OrderType.Sell;
            set => OrderSide = value == OrderType.Buy ? "buy" : "sell";
        }

        [DataMember(Name = "price")]
        [JsonProperty("price")]
        public decimal Price { get; set; }

        [DataMember(Name = "size")]
        [JsonProperty("size")]
        public decimal Amount { get; set; }

        public decimal Pending
        {
            get => Settled ? 0 : Amount;
            set { }
        }

        [DataMember(Name = "status")]
        [JsonProperty("status")]
        public string Status { get; set; }

        [DataMember(Name = "settled")]
        [JsonProperty("settled")]
        public bool Settled { get; set; }

        [DataMember(Name = "product_id")]
        [JsonProperty("product_id")]
        public string ProductId { get; set; }

        public string ExchangeCurrency
        {
            get => ProductId?.Split(new[] {"-"}, StringSplitOptions.RemoveEmptyEntries)?.ToList()?.FirstOrDefault();
            set { }
        }

        public string TargetCurrency
        {
            get => ProductId?.Split(new[] {"-"}, StringSplitOptions.RemoveEmptyEntries)?.ToList()
                ?.LastOrDefault();
            set { }
        }
    }
}