using Newtonsoft.Json;

namespace mleader.tradingbot.engines.Data.Gdax
{
    public class GdaxCurrencyLimit : CurrencyLimit
    {
        [JsonProperty("id")]
        public override string ExchangeCurrency { get; set; }
        public override string TargetCurrency { get; set; }
        [JsonProperty("min_size")]
        public override decimal? MinimumExchangeAmount { get; set; }
        public override decimal? MaximumExchangeAmount { get; set; }
        public override decimal? MinimumExchangePrice { get; set; }
        public override decimal? MaximumExchangePrice { get; set; }
    }
}