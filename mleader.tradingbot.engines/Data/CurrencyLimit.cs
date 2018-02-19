namespace mleader.tradingbot
{
    public abstract class CurrencyLimit
    {
        public abstract string ExchangeCurrency { get; set; }
        public abstract string TargetCurrency { get; set; }
        public abstract decimal? MinimumExchangeAmount { get; set; }
        public abstract decimal? MaximumExchangeAmount { get; set; }
        public abstract decimal? MinimumExchangePrice { get; set; }
        public abstract decimal? MaximumExchangePrice { get; set; }
    }
}