using Microsoft.AspNetCore.Mvc;
using stockapi.Entities;
using stockapi.Models.Stocks;
using stockapi.Services;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace stockapi.Controllers
{
    [Route("api/[controller]")]
    public class StocksController : BaseController
    {
        private readonly IStockScreenerService _stockService;

        public StocksController(IStockScreenerService stockService)
        {
            _stockService = stockService;
        }

        [Route("Search")]
        [HttpGet]
        public async Task<IActionResult> Search(string company)
        {
            IList<SearchCompanyResponse> listCompany = await _stockService.SearchCompany(company);

            return Ok(listCompany);
        }

        [Route("Sectors/all")]
        [HttpGet]
        public IActionResult GetAllCategories ()
        {
            string[] categories = { "healthcare", "technology", "financial", "industrials", "communicationservices" };

            IList<Company> listCompany = _stockService.GetStocks(categories);
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            JsonResult jsonResult = new JsonResult(listCompany, jsonOptions);

            return jsonResult;
        }

        [Route("Ideas/all")]
        [HttpGet]
        public IActionResult GetAllIdeas()
        {
            string[] ideas = { "dividends", "biotech", "growrecommendations"};

            IList<Company> listCompany = _stockService.GetStocks(ideas);
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            JsonResult jsonResult = new JsonResult(listCompany, jsonOptions);

            return jsonResult;
        }

        [Route("{category}")]
        [HttpGet]
        public IActionResult GetCategory(string category)
        {
            IList<Company> listCompany = _stockService.GetStocks(new string [] { category.ToLower()});
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            JsonResult jsonResult = new JsonResult(listCompany, jsonOptions);

            return jsonResult;
        }

        [Route("{category}")]
        [HttpPost]
        public async Task<IActionResult> UpdateCategory(string category)
        {
            await _stockService.UpdateStockCategory(category);

            return Ok();
        }

        [Route("AboutCompany")]
        public async Task<IActionResult> AboutCompany(string ticker)
        {
            Company company = await _stockService.GetBasePropOfCompany(ticker);

            return Ok(company);
        }

        [Route("CompSummary")]
        [HttpGet]
        public async Task<IActionResult> CompSummary(string ticker)
        {
            JsonElement root = await _stockService.GetCompleteStockInfo(ticker);

            return Ok(root);
        }

        [Route("getchart")]
        [HttpGet]
        public async Task<IActionResult> GetChart (string stockId)
        {
            JsonElement root = await _stockService.GetChart(stockId);

            return Ok(root);
        }

        [Route("getindicators")]
        [HttpGet]
        public IActionResult GetIndicatorsVsIndustry(string ticker)
        {
            JsonElement result = _stockService.GetIndicatorsVsIndustry(ticker);

            return Ok(result);
        }

        [Route("getfinancials/{ticker}")]
        [HttpGet]
        public IActionResult GetFinancials (string ticker)
        {
            JsonElement result = _stockService.GetFinancials(ticker);

            return Ok(result);
        }
        
        [Route("analysts-recommendations/{ticker}")]
        [HttpGet]
        public async Task<IActionResult> GetAnalystsRecommendations (string ticker)
        {
            JsonElement root = await _stockService.GetAnalystsRecommendations(ticker);

            return Ok(root);
        }

        [Route("GetPrices/{ticker}")]
        [HttpGet]
        public async Task<IActionResult> GetPrices(string ticker)
        {
            JsonElement root = await _stockService.GetPrices(ticker);

            return Ok(root);
        }
    } 
}
