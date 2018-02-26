using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace mleader.tradingbot.engines.Data.Bitstamp
{
    public class BitstampOrderbook : Orderbook
    {
        [JsonProperty("timestamp")] public override double Timestamp { get; set; }

        [JsonProperty("bids")] public override List<List<decimal>> Bids { get; set; }

        [JsonProperty("asks")] public override List<List<decimal>> Asks { get; set; }

        [JsonProperty("pair")] public override string Pair { get; set; }

        [JsonProperty("id")] public override string Id { get; set; }

        [JsonIgnore]
        [IgnoreDataMember] public override decimal SellTotal
        {
            get => (Asks?.Sum(item => item[1])).GetValueOrDefault();
            set { }
        }

        [JsonIgnore]
        [IgnoreDataMember] public override decimal BuyTotal
        {
            get => (Bids?.Sum(item => item[1])).GetValueOrDefault();
            set { }
        }

        [JsonIgnore]
        [IgnoreDataMember]
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