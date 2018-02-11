using System;

namespace mleader.tradingbot.Data
{
    public interface ITradeHistory
    {
        string Id { get; set; }

        OrderType OrderType { get; set; }
        decimal Amount { get; set; }
        decimal Price { get; set; }
        DateTime Timestamp { get; }
    }
}