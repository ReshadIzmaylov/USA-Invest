using ScrapySharp.Extensions;
using ScrapySharp.Network;
using stockapi.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace stockapi.Helpers
{
    public class StocksWebScraping
    {
        private static string[] indicators = { "Cash-To-Debt", "Equity-to-Asset", "Debt-to-Equity", "Interest Coverage",
                "Operating Margin %", "Net Margin %", "ROE %", "ROA %", "ROC (Joel Greenblatt) %", "3-Year Revenue Growth Rate",
                "PE Ratio", "Forward PE Ratio", "PB Ratio", "PS Ratio", "PEG Ratio", "Current Ratio", "Quick Ratio" };
        private static string[] indicatorsWidth = { "cash2debt_width", "equity2asset_width", "debt2equity_width", "interest_coverage_width",
                "oprt_margain_width", "net_margain_width", "roe_width", "roa_width", "ROC_JOEL_width", "rwn_growth_3y_width",
                "pe_width", "forwardPE_width", "pb_width", "ps_width", "peg_width", "current_ratio_width", "quick_ratio_width" };
        private static string[] financialData = { "Revenue", "Net Income", "Cash, Cash Equivalents, Marketable Securities",
                "Cash and cash equivalents", "Short-term investments", "Long-Term Debt & Capital Lease Obligation", "Short-Term Debt & Capital Lease Obligation"};

        public int GetValuationRank(string ticker)
        {
            WebPage webPage = GetWebPage($"https://www.gurufocus.com/stock/{ticker}/summary");

            int ValRank = 0;
            var divs = webPage.Html.CssSelect("div");
            var divRatios = divs.FirstOrDefault(x => x.Attributes["id"]?.Value == "ratios");
            if (divRatios != null)
            {
                var tdElements = divRatios.CssSelect("td");
                var rgx = new System.Text.RegularExpressions.Regex(@"\d/\d");
                var tdValRank = tdElements.FirstOrDefault(x => rgx.IsMatch(x.InnerText));
                if (tdValRank != null)
                {
                    int.TryParse(tdValRank.InnerText.Split('/').FirstOrDefault(), out ValRank);
                }
            }
            return ValRank;
        }

        public void GetLogoAndDescription (string ticker, out string description, out string logoLink)
        {
            WebPage webPage = GetWebPage($"https://www.tinkoff.ru/invest/stocks/{ticker}/");

            description = GetDescription(webPage);
            logoLink = GetLogo(webPage, ticker);
        }

        private string GetDescription (WebPage webPage)
        {
            // грузим описание компании с сайта Тинькофф
            string compDescription = string.Empty;
            var divs = webPage.Html.CssSelect("div");
            var divInfo = divs.FirstOrDefault(x => x.Attributes["data-qa-file"]?.Value == "SecurityInfoPure");

            // копируем описание компании и удаляем оттуда ненужную строку
            compDescription = divInfo?.InnerText.Replace("Официальный сайт компании", "");

            return compDescription;
        }

        private string GetLogo(WebPage webPage, string ticker)
        {
            string logoLink = string.Empty;

            // пытаемся найти ссылку на лого на сайте Тинькофф
            var divLogo = webPage.Html.CssSelect(".Avatar-module__root_size_xl_1wB6V");
            var spanLogo = divLogo?.CssSelect(".Avatar-module__image_ZCGVO");

            if (spanLogo.Any())
            {
                logoLink = spanLogo.First().Attributes["style"]?.Value ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(logoLink))
            {
                // заменяем ненужную часть ссылки на https и удаляем последний символ ")"
                logoLink = logoLink.Replace("background-image:url(", "https:");
                logoLink = logoLink.Remove(logoLink.Length - 1);
            }
            else
            {
                logoLink = $"https://storage.googleapis.com/iex/api/logos/{ticker}.png";
            }

            return logoLink;
        }

        public List<string> GetTickersFromFinviz(IEnumerable<string> links)
        {
            var listPages = new List<WebPage>();
            var tickers = new List<string>();
            foreach (string link in links)
            {
                listPages.Add(GetWebPage(link));
            }

            foreach (WebPage page in listPages)
            {
                var tickerNodes = page.Html.CssSelect(".screener-link-primary");

                foreach (var tickerNode in tickerNodes)
                {
                    tickers.Add(tickerNode.InnerText);
                }
            }

            return tickers;
        }

        public Dictionary<string, double> GetIndicatorsVsIndustry(string ticker)
        {
            var dictIndicators = new Dictionary<string, double>(indicators.Length);
            WebPage page= GetWebPage($"https://www.gurufocus.com/stock/{ticker}/summary");
            try
            {
                // вытаскиваем табличные строки с индикаторами
                var trIndicators = page.Html.CssSelect(".stock-indicators-table-row");

                for (int i = 0; i < indicators.Length; i++)
                {
                    foreach (var trIndicator in trIndicators)
                    {
                        if (trIndicator.InnerText.Contains(indicators[i]))
                        {
                            // TODO: проверить ошибочным запросом может ли здесь вернуться null, если нет - убрать проверки и переделать
                            var progressBar = trIndicator.CssSelect(".indicator-progress-bar");
                            var divVsIndustry = progressBar?.First().FirstChild;
                            // интересующий нас параметр vsIndustry отображен в ширине индикатора, 
                            // а он в свою очередь записан в аттрибуте style вместе с другими параметрами
                            string strProgressBar = divVsIndustry?.Attributes["style"]?.Value;
                            string strVsIndustry = strProgressBar?.Substring(0, strProgressBar.IndexOf(';'));
                            // удаляем лишнее из строки strVsIndustry и преобразовываем в значение double
                            var numStyle = NumberStyles.AllowDecimalPoint;
                            var culture = new CultureInfo("en-GB");
                            bool isParsed = Double.TryParse(
                                strVsIndustry?.Replace("width:", "").Replace("%", ""),
                                numStyle,
                                culture,
                                out double vsIndustryValue);
                            if (isParsed)
                                vsIndustryValue = Math.Round(vsIndustryValue, 2);
                            dictIndicators.Add(indicatorsWidth[i], vsIndustryValue);
                            break;
                        }
                    }
                }
                return dictIndicators;
            }
            catch
            {
                return new Dictionary<string, double>();
            }
        }

        // сложный метод, в ходе которого мы вытаскиваем данные по финансовым показателям financialData с gurufocus.com
        public string GetFinancials(string ticker)
        {
            WebPage page = GetWebPage($"https://www.gurufocus.com/financials/{ticker}");
            int numberOfQuarters = 5;
            int numberOfYears = 4;
            int quarterlyOffset = 6;
            int yearOffset = 1;

            var finYearGurufocusModel = new FinYearGurufocusModel();
            var finQuartGurufocusModel = new FinQuartGurufocusModel();

            var tdFiscalPer = page.Html.CssSelect("td").FirstOrDefault(x => x.Attributes["title"]?.Value == "Fiscal Period");
            if (tdFiscalPer != null)
            {
                var trFiscPer = tdFiscalPer.ParentNode;
                var tdFiscalPeriods = trFiscPer.CssSelect("td").Where(x => x.Attributes["title"]?.Value != null);

                // вытаскиваем квартальные даты
                var quarters = new string[numberOfQuarters];
                for (int i = 0; i < quarters.Length; i++)
                {
                    string dateQuarterly = tdFiscalPeriods.ToList()[i + quarterlyOffset]?.GetAttributeValue("title", "");
                    dateQuarterly = dateQuarterly.Substring(0, 5); // берем первые 5 символов (MMMyy), т.к. только они значащие
                    string monthQuart = DateTime.ParseExact(dateQuarterly, "MMMyy", CultureInfo.InvariantCulture).Month.ToString();
                    string yearQuart = DateTime.ParseExact(dateQuarterly, "MMMyy", CultureInfo.InvariantCulture).Year.ToString();
                    quarters[i] = monthQuart + "/" + yearQuart;
                }
                finQuartGurufocusModel.Quarterly = quarters;

                // берем первый период, чтобы знать с какого года вести отсчет (всего можем предоставить данные за 4 года)
                string startDate = tdFiscalPeriods.ToList()[yearOffset].GetAttributeValue("title", "");
                finYearGurufocusModel.startYear = DateTime.ParseExact(startDate, "MMMyy", CultureInfo.InvariantCulture).Year;
            }

            bool noMarketableSecurities = false;
            foreach (var param in financialData)
            {
                var tds = page.Html.CssSelect("td");
                var tdParam = tds.FirstOrDefault(x => x.Attributes["title"]?.Value == param);
                if (tdParam != null)
                {
                    var trParam = tdParam.ParentNode;
                    List<HtmlAgilityPack.HtmlNode> listValues = trParam
                        .CssSelect("td")
                        .Where(x => x.Attributes["title"]?.Value != null)
                        .ToList();

                    var dYearValues = new double[numberOfYears];
                    var dQuartValues = new double[numberOfQuarters];
                    var style = NumberStyles.Number | NumberStyles.AllowCurrencySymbol;
                    var culture = new CultureInfo("en-GB");
                    // заполняем годовые данные
                    for (int i = 0; i < numberOfYears; i++)
                    {
                        Double.TryParse(
                            listValues[i + yearOffset].Attributes["title"].Value,
                            style,
                            culture,
                            out dYearValues[i]);
                    }
                    // заполняем квартальные данные
                    for (int i = 0; i < numberOfQuarters; i++)
                    {
                        // здесь смещение для получения квартальных данных уже должно быть на 1 меньше,
                        // т.к. показатель TTM в listValues уже не попадает (см. ссылку на gurufocus и разбирай таблицу)
                        Double.TryParse(
                            listValues[i + (quarterlyOffset -1)].Attributes["title"].Value,
                            style,
                            culture,
                            out dQuartValues[i]);
                    }

                    // округляем все данные до 1 знака после запятой
                    dYearValues = dYearValues.Select(d => Math.Round(d, 1)).ToArray();
                    dQuartValues = dQuartValues.Select(d => Math.Round(d, 1)).ToArray();

                    // копируем ссылки на получившиеся массивы с данными в соответствующие поля моделей
                    switch (param)
                    {
                        case "Revenue":
                            finYearGurufocusModel.RevenueData = dYearValues;
                            finQuartGurufocusModel.RevenueData = dQuartValues;
                            break;
                        case "Net Income":
                            finYearGurufocusModel.IncomeData = dYearValues;
                            finQuartGurufocusModel.IncomeData = dQuartValues;
                            break;
                        case "Cash, Cash Equivalents, Marketable Securities":
                            finYearGurufocusModel.CashData = dYearValues;
                            finQuartGurufocusModel.CashData = dQuartValues;
                            break;
                        case "Cash and cash equivalents":
                            // для некоторых акций "Cash, Cash Equivalents, Marketable Securities" отсутствует,
                            // в таких случаях CashData формируется путем сложения "Cash and cash equivalents" с "Short-term investments"
                            if (finYearGurufocusModel.CashData == null)
                            {
                                finYearGurufocusModel.CashData = dYearValues;
                                finQuartGurufocusModel.CashData = dQuartValues;
                                noMarketableSecurities = true;
                            }
                            break;
                        case "Short-term investments":
                            if (noMarketableSecurities)
                            {
                                for (int i = 0; i < numberOfYears; i++)
                                {
                                    finYearGurufocusModel.CashData[i] += dYearValues[i];
                                    finQuartGurufocusModel.CashData[i] += dQuartValues[i];
                                }
                                // записываем данные последнего квартала
                                finQuartGurufocusModel.CashData[numberOfQuarters - 1] += dQuartValues[numberOfQuarters - 1];    
                            }
                            break;
                        case "Long-Term Debt & Capital Lease Obligation":
                            // показатель DebtData формируется путем сложения 
                            // "Long-Term Debt & Capital Lease Obligation" с "Short-Term Debt & Capital Lease Obligation"
                            finYearGurufocusModel.DebtData = dYearValues;
                            finQuartGurufocusModel.DebtData = dQuartValues;
                            break;
                        case "Short-Term Debt & Capital Lease Obligation":
                            for (int i = 0; i < numberOfYears; i++)
                            {
                                finYearGurufocusModel.DebtData[i] += dYearValues[i];
                                finQuartGurufocusModel.DebtData[i] += dQuartValues[i];
                            }
                            // записываем данные последнего квартала
                            finQuartGurufocusModel.DebtData[numberOfQuarters - 1] += dQuartValues[numberOfQuarters - 1];
                            break;
                    }
                }
            }
            string result = string.Empty;
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            result = "{\"yearData\":" + JsonSerializer.Serialize<FinYearGurufocusModel>(finYearGurufocusModel, jsonOptions) +
                "," + "\"quarterlyData\":" + JsonSerializer.Serialize<FinQuartGurufocusModel>(finQuartGurufocusModel, jsonOptions) + "}";
            return result;
        }

        // вспомогательные методы

        private WebPage GetWebPage(string strUri)
        {
            ScrapingBrowser browser = new ScrapingBrowser();
            WebPage webPage;
            try
            {
                webPage = browser.NavigateToPage(new Uri(strUri));
                return webPage;
            }
            catch (Exception ex)
            {
                throw new AppException(ex.Message);
            }
        }
    }
}
