# cryptocurrency-trading-bot
A trading bot created for trading on exchanges (currently only support CEX.IO)


## Disclaimer
This is a working bot using strategy and logics that fits the developer's own profit growth purpose, it does not guarantee anyone including the developer himself profit increase and may result serious investment loss if not monitored and used properly.

**Use at your own risk**

## How to use it

The bot is build using .NET Core 2.0 so it's compatible in Windows, Linux, macOS. 
```
dotnet restore
dotnet run
```
You will be asked to set up your Api Key, Secrets etc. at the begining each time when you run the bot. *Leave it running and do the trading unmonitored ONLY WHEN YOU ARE CONFIDENT ENOUGH*. 

Setting your API Keys can be done via https://cex.io/trade/profile#/api Make sure you *activate* your keys before using it.


## Contribution
You are more than welcomed to contribute more into the strategy design or trading features needed in the bot. Currently it only supports CEX.IO trading exchange. 

The bot is designed to cope with multiple trading platforms, your contribution of adding more supported exchanges are welcomed.
