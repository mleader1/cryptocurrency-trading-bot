using System;

namespace mleader.tradingbot
{
    public static class DateExtensions
    {
        public static double ToTimeStamp(this DateTime date)
        {
            return (date - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public static DateTime ToDateTime(this double unixTimeStamp)
        {
            try
            {
                var convertDateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                convertDateTime = convertDateTime.AddMilliseconds(unixTimeStamp).ToLocalTime();
                return convertDateTime;
            }
            catch (Exception ex)
            {
            }

            return default(DateTime);
        }
    }
}