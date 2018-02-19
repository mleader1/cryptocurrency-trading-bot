using System.Collections.Generic;
using Newtonsoft.Json;

namespace mleader.tradingbot.Data.Cex
{
    public class CexCurrencyLimit : CurrencyLimit
    {
        [JsonProperty("symbol1")]
        public override string ExchangeCurrency { get; set; }

        [JsonProperty("symbol2")]
        public override string TargetCurrency { get; set; }

        [JsonProperty("minLotSize")]
        public override decimal? MinimumExchangeAmount { get; set; }

        [JsonProperty("maxLotSize")]
        public override decimal? MaximumExchangeAmount { get; set; }

        [JsonProperty("minPrice")]
        public override decimal? MinimumExchangePrice { get; set; }

        [JsonProperty("maxPrice")]
        public override decimal? MaximumExchangePrice { get; set; }
    }
}