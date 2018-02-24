using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Schema;
using mleader.tradingbot.Data;
using Newtonsoft.Json;

namespace mleader.tradingbot.engines.Data.Bitstamp
{
    public class BitstampUserTransaction : IOrder
    {
        [DataMember(Name = "Id")]
        [JsonProperty("id")]
        public string TransactionId { get; set; }

        [DataMember(Name = "order_id")]
        [JsonProperty("order_id")]
        public string OrderId { get; set; }

        [DataMember(Name = "datetime")]
        [JsonProperty("datetime")]
        public DateTime DateTime { get; set; }

        public DateTime Timestamp => DateTime;

        [JsonProperty("usd")]
        [DataMember(Name = "usd")]
        public decimal USD { get; set; }

        [JsonProperty("eur")]
        [DataMember(Name = "eur")]
        public decimal EUR { get; set; }

        [JsonProperty("btc")]
        [DataMember(Name = "btc")]
        public decimal BTC { get; set; }

        [JsonProperty("xrp")]
        [DataMember(Name = "xrp")]
        public decimal XRP { get; set; }

        [JsonProperty("btc_usd")]
        [DataMember(Name = "btc_usd")]
        public decimal PriceUSD { get; set; }

        [JsonProperty("btc_eur")]
        [DataMember(Name = "btc_eur")]
        public decimal PriceEUR { get; set; }

        [JsonProperty("fee")]
        [DataMember(Name = "fee")]
        public decimal Fee { get; set; }


        public decimal Price
        {
            get => PriceUSD > 0 ? PriceUSD : PriceEUR;
            set { }
        }


        [JsonIgnore]
        [IgnoreDataMember] public OrderType Type
        {
            get => USD > 0 || EUR > 0 ? OrderType.Buy : OrderType.Sell;
            set { }
        }


        public decimal Amount
        {
            get => Type == OrderType.Buy
                ? (USD > 0 && PriceUSD > 0 ? USD / PriceUSD : (EUR > 0 && PriceEUR > 0 ? EUR / PriceEUR : 0))
                : (BTC > 0 ? BTC : XRP);
            set { }
        }

        public decimal Pending { get; set; }

        [DataMember(Name = "status")]
        [JsonProperty("status")]
        public string Status { get; set; }

        public string ExchangeCurrency
        {
            get => BTC > 0 ? "BTC" : "XRP";
            set { }
        }

        public string TargetCurrency
        {
            get => USD > 0 ? "USD" : "EUR";
            set { }
        }
    }
}