using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace mleader.tradingbot.engines.Data.Gdax
{
    public class GdaxTrailingVolume
    {
        [JsonProperty("product_id")] public string ProductId { get; set; }

        public string ExchangeCurrency =>
            ProductId?.Split(new[] {"-"}, StringSplitOptions.RemoveEmptyEntries)?.ToList()?.FirstOrDefault();

        public string TargetCurrency => ProductId?.Split(new[] {"-"}, StringSplitOptions.RemoveEmptyEntries)?.ToList()
            ?.LastOrDefault();

        [JsonProperty("exchange_volume")] public decimal ExchangeVolume { get; set; }

        [JsonProperty("volume")] public decimal Volume { get; set; }
    }

    public class GdaxTradingFee
    {
        public string ExchangeCurrency { get; set; }
        public decimal TradingPercent { get; set; }
        public decimal TakerFeeInPercent { get; set; }
        public decimal MakerFeeInPercent { get; set; }
    }

    public static class GdaxTradingFeeStructure
    {
        public static readonly List<GdaxTradingFee> FeeLevels = new List<GdaxTradingFee>
        {
            new GdaxTradingFee {TradingPercent = 0, TakerFeeInPercent = 0.0025m},
            new GdaxTradingFee {TradingPercent = 0.01m, TakerFeeInPercent = 0.0025m},
            new GdaxTradingFee {TradingPercent = 0.025m, TakerFeeInPercent = 0.0025m},
            new GdaxTradingFee {TradingPercent = 0.05m, TakerFeeInPercent = 0.0025m},
            new GdaxTradingFee {TradingPercent = 0.1m, TakerFeeInPercent = 0.0025m},
            new GdaxTradingFee {TradingPercent = 0.2m, TakerFeeInPercent = 0.0025m}
        };

        public static GdaxTradingFee GetTradingFee(this GdaxTrailingVolume volume)
        {
            var tradingFee = default(GdaxTradingFee);
            if (volume?.ExchangeVolume > 0)
            {
                var pct = volume.Volume / volume.ExchangeVolume;
                foreach (var lv in FeeLevels)
                {
                    if (pct >= lv.TradingPercent) tradingFee = lv;
                }
            }

            return tradingFee ?? FeeLevels.FirstOrDefault();
        }
    }
}