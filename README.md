# cryptocurrency-trading-bot
A trading bot created for trading on exchanges (currently only support CEX.IO)

**Console**

![Bot Running In Production](https://raw.githubusercontent.com/mleader1/cryptocurrency-trading-bot/master/demo.png)

**Integration with Slack**

![Bot Notification to Slack](https://raw.githubusercontent.com/mleader1/cryptocurrency-trading-bot/master/demo-slack.png)

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

The bot will keep running till you stop; If Manual Execution Mode, You will need to confirm whether to execute or skip for *every suggested order* in order to keep the bot working with the next update (i.e. retrieving data from CEX and do the magic).

## Limits

- The Live Data is limited by CEX.IO API. Particularly the account archived orders are not always up-to-date. Therefore I used the last 24 hours data feed (orders done on CEX and orders executed under your account) as the base.  You can change the hours parameters in *TradingStrategy*, which is instantiated at Startup (*Program.cs*).

- The API Request threashold is limited by CEX.IO for 600 requests per 10 minutes. The bot currently works out the total requests executed and then automatically pause & wait till allowance is refilled.

- The Trading Engine I created for CEX is based on my own personal need. If you wish, you can implement your own TradingEngine using *ITradingEngine*.  TradingEngine is used to *1) Implementation for different trading exchange platforms, 2) Implementation for different trading logics based on different trading strategies, 3) Implementation for different trading currency pairs*

- As stated above, each TradingEngine will work only on 1 pair of trading currencies. If you want to trade multiple currenties, you just need to instantiate multiple TradingEngines and run them simultaneously. 

## LOGIC NOTE

**The note below is for technical reference only.** It roughly records how I worked out my trading logics. **It is by no means PROFESSIONAL trader logic** - as I am a developer, not a real trader.  No established trading algorithm is used in the TradingEngine I created.

**Do not ask me to implement any algorithm or trading logics, you have the code here, please help yourself - but contribution back to the repository is highly appreciated.**

### Automated logics:
1. Identify how much amount can be spent for the next order
2. Identify how much commission/fee (percentage) will be charged for the next order
3. Identify the correct amount to be spent for the next order (using historical order)
4. If Reserve amount after order is lower than the minimum reserve amount calculated based on percentage then drop the order, otherwise execute the order

### Price decision making factors:
1. fetch X number of historical orders to check their prices
2. setting the decision factors:

        2.1  [PublicWeightedAverageSellPrice] Find the last X records of public sale prices and do a weighted average
        2.2  [PublicWeightedAverageBestSellPrice] Find the best weighted average selling price of the 1/3 best sellig prices
        2.3  [PublicWeightedAverageLowSellPrice]  Find the poorest weighted average selling price of the 1/3 low selling prices
        2.4  [PublicLastSellPrice] Find the last public sale price
        2.5  [PublicWeightedAveragePurchasePrice] Find the last X records of public purchase prices and do a weighted average
        2.6  [PublicWeightedAverageBestPurchasePrice] Find the best weighted average purchase price of the 1/3 lowest purchase prices
        2.7  [PublicWeightedAverageLowPurchasePrice] Find the poorest weighted average purchase price of the 1/3 highest purchase prices
        2.8  [PublicLastPurchasePrice] Find the last public purchase price
        2.9  [AccountWeightedAveragePurchasePrice] Find the last X record of account purchase prices and do a weighted average (i.e. should sell higher than this)
        2.10 [AccountWeightedAverageSellPrice] Find the last Y records of account sale prices and do a weighted average
        2.11 [BuyingFeeInPercentage] My trading buying fee calculated in percentage
        2.12 [SellingFeeInPercentage] My selling fee calculated in percentage
        2.13 [BuyingFeeInAmount] My buying fee calculated in fixed amount
        2.14 [SellingFeeInAmount] My selling fee calculated in fixed amount

**LOGIC, Decide if the market is trending price up**

        [PublicUpLevelSell1] = ABS([PublicWeightedAverageBestSellPrice] - [PublicWeightedAverageSellPrice]) / [PublicWeightedAverageSellPrice]
        [PublicUpLevelSell2] = ABS([PublicWeightedAverageLowSellPrice] - [PublicWeightedAverageSellPrice]) / [PublicWeightedAverageSellPrice]
        [PublicUpLevelPurchase1] = ABS([PublicWeightedAverageBestPurchasePrice] - [PublicWeightedAveragePurchasePrice]) / [PublicWeightedAveragePurchasePrice]
        [PublicUpLevelPurchase2] = ABS([PublicWeightedAverageLowPurchasePrice] - [PublicWeightedAveragePurchasePrice]) / [PublicWeightedAveragePurchasePrice] 
       
        [IsPublicUp] = [PublicUpLevelPurchase1] >= [PublicUpLevelSell1] && [PublicUpLevelPurchase2] <= [PublicUpLevelPurchase2]
        [AverageTradingChangeRatio] = AVG([PublicUpLevelSell1] , [PublicUpLevelSell2], [PublicUpLevelPurchase1], [PublicUpLevelPurchase2])


3. when selling:

        3.1 [ProposedSellingPrice] = MAX(AVG([PublicWeightedAverageSellPrice],[PublicLastSellPrice], [AccountWeightedAveragePurchasePrice], [AccountWeightedAverageSellPrice]), [PublicLastSellPrice])
        3.2 [SellingPriceInPrinciple] = [ProposedSellingPrice] * (1+ [SellingFeeInPercentage] + [AverageTradingChangeRatio] * ([IsPublicUp]? 1: -1)) + [SellingFeeInAmount]

4. when buying:

        4.1 [ProposedPurchasePrice] = MIN(AVG([PublicWeightedAveragePurchasePrice],[PublicLastPurchasePrice], [PublicWeightedAverageBestPurchasePrice], [AccountWeightedAverageSellPrice]), [PublicLastPurchasePrice])
        4.2 [PurchasePriceInPrinciple] = [ProposedPurchasePrice] * (1 - [BuyingFeeInPercentage] + [AverageTradingChangeRatio] * ([IsPublicUp] ? 1: -1)) + [BuyingFeeInAmount]

**Final Decision:**
5. If final portfolio value is descreasing, do not buy/sell. (Plus all the preset parameters from user input, calculated to prevent profit loss and risks)



## Contribution
You are more than welcomed to contribute more into the strategy design or trading features needed in the bot. Currently it only supports CEX.IO trading exchange. 

The bot is designed to cope with multiple trading platforms, your contribution of adding more supported exchanges are welcomed.
