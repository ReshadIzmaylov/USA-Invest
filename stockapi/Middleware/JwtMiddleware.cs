using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using stockapi.Helpers;

namespace stockapi.Middleware
{
    public class JwtMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AppSettings _appSettings;

        public JwtMiddleware(RequestDelegate next, IOptions<AppSettings> appSettings)
        {
            _next = next;
            _appSettings = appSettings.Value;
        }

        public async Task Invoke(HttpContext context, DataContext dataContext)
        {
            //var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
            var token = context.Request.Cookies["jwt"];
            if (token != null)
                await attachAccountToContext(context, dataContext, token);

            await _next(context);
        }

        private async Task attachAccountToContext(HttpContext context, DataContext dataContext, string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_appSettings.Secret);
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    // устанавливаем clockskew на 0 чтобы срок действия токенов истекал точно тогда, когда должен, вместо 300 сек позже (по умолчанию)
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                var accountId = int.Parse(jwtToken.Claims.First(x => x.Type == "id").Value);

                // прикрепляем аккаунт к контексту при успешной проверке jwt
                context.Items["Account"] = await dataContext.Accounts.FindAsync(accountId);
            }
            catch
            {
                // ничего не делаем если jwt не прошел проверку
                // аккаунт не прикрепится к контексту, поэтому запрос не будет иметь доступа к защищенным маршрутам
            }
        }
    }
}
