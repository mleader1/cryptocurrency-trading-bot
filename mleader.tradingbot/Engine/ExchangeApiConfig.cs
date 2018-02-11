using Microsoft.Extensions.Logging;

namespace mleader.tradingbot.Engine
{
    public class ExchangeApiConfig
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string ApiUsername { get; set; }

        public ILogger Logger { get; set; }
    }
}