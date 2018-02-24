namespace mleader.tradingbot.engines.Data.Bitstamp
{
    public class BitstampCurrencyLimit : CurrencyLimit
    {
        public override string ExchangeCurrency { get; set; }

        public override string TargetCurrency { get; set; }

        public override decimal? MinimumExchangeAmount { get; set; }

        public override decimal? MaximumExchangeAmount { get; set; }

        public override decimal? MinimumExchangePrice { get; set; }

        public override decimal? MaximumExchangePrice { get; set; }

    }
}