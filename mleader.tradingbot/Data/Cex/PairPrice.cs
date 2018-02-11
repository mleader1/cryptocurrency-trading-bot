using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace mleader.tradingbot.Data.Cex
{
    public class PairPrice : ICurrencyPrice
    {
        [JsonProperty("lprice")]
        public decimal Price { get; set; }

        [JsonProperty("curr1")]
        public string ExchangeCurrency { get; set; }

        [JsonProperty("curr2")]
        public string TargetCurrency { get; set; }

        public CurrencyPrice ConvertToBase() =>
            new CurrencyPrice
            {
                Currency = ExchangeCurrency,
                TargetCurrency = TargetCurrency,
                ExchangePrice = Price
            };
    }
}