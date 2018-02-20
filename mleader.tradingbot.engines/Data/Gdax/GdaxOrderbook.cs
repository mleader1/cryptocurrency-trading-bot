using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using OElite;

namespace mleader.tradingbot.engines.Data.Gdax
{
    public class GdaxOrderbook : Orderbook
    {
        public override double Timestamp { get; set; }

        [DataMember(Name = "asks")]
        [JsonProperty("asks")]
        public List<dynamic[]> GdaxAsks { get; set; }

        [DataMember(Name = "bids")]
        [JsonProperty("bids")]
        public List<dynamic[]> GdaxBids { get; set; }

        [IgnoreDataMember]
        [JsonIgnore] public override List<List<decimal>> Bids
        {
            get => GdaxBids?.Select(item => new List<decimal>
            {
                NumericUtils.GetDecimalValueFromObject(item[0]),
                NumericUtils.GetDecimalValueFromObject(item[1]) * item[2]
            }).ToList();
            set { }
        }

        [IgnoreDataMember]
        [JsonIgnore] public override List<List<decimal>> Asks
        {
            get => GdaxAsks?.Select(item => new List<decimal>
            {
                NumericUtils.GetDecimalValueFromObject(item[0]),
                NumericUtils.GetDecimalValueFromObject(item[1]) * item[2]
            }).ToList();
            set { }
        }

        public override string Pair { get; set; }
        [JsonProperty("sequence")] public override string Id { get; set; }

        public override decimal SellTotal
        {
            get => (Asks?.Sum(item => item[0] * item[1])).GetValueOrDefault();
            set { }
        }

        public override decimal BuyTotal
        {
            get => (Bids?.Sum(item => item[0] * item[1])).GetValueOrDefault();
            set { }
        }

        public override DateTime OrderTime => DateTime.Now;
    }
}