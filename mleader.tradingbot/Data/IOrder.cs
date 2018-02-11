using System;

namespace mleader.tradingbot.Data
{
    public interface IOrder
    {
        string OrderId { get; set; }
        DateTime Timestamp { get; }
        OrderType Type { get; set; }
        decimal Price { get; set; }
        decimal Amount { get; set; }
        decimal Pending { get; set; }
        string ExchangeCurrency { get; set; }
        string TargetCurrency { get; set; }
    }
}