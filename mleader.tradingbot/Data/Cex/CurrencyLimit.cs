using System.Collections.Generic;
using Newtonsoft.Json;

namespace mleader.tradingbot.Data.Cex
{
    public class CurrencyLimit
    {
        [JsonProperty("symbol1")]
        public string ExchangeCurrency { get; set; }

        [JsonProperty("symbol2")]
        public string TargetCurrency { get; set; }

        [JsonProperty("minLotSize")]
        public decimal? MinimumExchangeAmount { get; set; }

        [JsonProperty("maxLotSize")]
        public decimal? MaximumExchangeAmount { get; set; }

        [JsonProperty("minPrice")]
        public decimal? MinimumExchangePrice { get; set; }

        [JsonProperty("maxPrice")]
        public decimal? MaximumExchangePrice { get; set; }
    }
}