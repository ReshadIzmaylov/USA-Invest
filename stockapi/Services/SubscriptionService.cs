using stockapi.Entities;
using stockapi.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace stockapi.Services
{
    public interface ISubscriptionService
    {
        string BuySubscription(int accountId, string subscriptionType);
        IEnumerable<Subscription> GetAllSubscriptions();
        DateTime CheckSubscriptionEndDate(int id);
        void SetSubscriptionEndDate(int id, int numberOfMonths);
        PromoCode CreatePromocode(int additionalDays, DateTime expirationDate);
        IEnumerable<PromoCode> GetAllPromocodes();
        void UsePromocode(int id, string code);
    }

    public class SubscriptionService : ISubscriptionService
    {
        private readonly DataContext _context;
        public SubscriptionService(DataContext context)
        {
            _context = context;
        }

        public string BuySubscription(int accountId, string subscriptionType)
        {
            string subscriptionTypes = System.IO.File.ReadAllText("SubscriptionTypes.json");
            int subscriptionCost;
            int subscriptionDuration;
            string subscriptionDescription;

            using (JsonDocument doc = JsonDocument.Parse(subscriptionTypes))
            {
                JsonElement root = doc.RootElement;
                JsonElement jsSubsType = root.GetProperty(subscriptionType);

                subscriptionCost = jsSubsType.GetProperty("cost").GetInt32();
                subscriptionDuration = jsSubsType.GetProperty("duration").GetInt32();
                subscriptionDescription = jsSubsType.GetProperty("description").GetString();
            }

            if (subscriptionCost == 0 || subscriptionDuration == 0)
                throw new AppException("Ошибка в указании типа подписки");

            // Сохраняем информацию о покупке в БД
            
            Subscription newSubscription = new Subscription
            {
                UserId = accountId,
                OrderId = "", // здесь должен быть уникальный номер заказа
                Type = subscriptionType,
                Duration = subscriptionDuration,
                Cost = subscriptionCost,
                OrderDate = DateTime.Now
            };

            _context.Subscriptions.Add(newSubscription);
            _context.SaveChanges();

            // далее формируется ссылка для перенаправления на страницу оплаты, после чего она возвращается из метода.
            throw new NotImplementedException("В данный момент функционал оплаты реализован не полностью (из-за проблем с онлайн кассой)");
        }

        public IEnumerable<Subscription> GetAllSubscriptions()
        {
            var subscriptions = _context.Subscriptions;
            return subscriptions.ToList();
        }

        public void SetSubscriptionEndDate (int id, int numberOfMonths)
        {
            var account = GetAccount(id);

            // если мы даем подписку обычному пользователю, то меняем его роль на UserWithSub
            if (account.Role == Role.User)
            {
                account.Role = Role.UserWithSub;
                account.SubscriptionEndDate = DateTime.Now.AddMonths(numberOfMonths);
            }
            else if (account.Role == Role.UserWithSub)
            {
                account.SubscriptionEndDate = account.SubscriptionEndDate.Value.AddMonths(numberOfMonths);
            }

            _context.Accounts.Update(account);
            _context.SaveChanges();
        }

        public PromoCode CreatePromocode (int additionalDays, DateTime expirationDate)
        {
            string code = GenerateRandomString(10);
            PromoCode newPromoCode = new PromoCode
            {
                Code = code,
                AdditionalDays = additionalDays,
                ExpirationDate = expirationDate
            };

            _context.PromoCodes.Add(newPromoCode);
            _context.SaveChanges();
            return newPromoCode;
        }

        public IEnumerable<PromoCode> GetAllPromocodes()
        {
            var promocodes = _context.PromoCodes;
            return promocodes.ToList();
        }

        public void UsePromocode (int id, string code)
        {
            var account = GetAccount(id);
            PromoCode promo = _context.PromoCodes.SingleOrDefault(x => x.Code == code);
            
            if (promo == null || promo.ExpirationDate < DateTime.Now)
                throw new AppException("Промокод недействителен");

            if (account.Role == Role.User)
            {
                account.Role = Role.UserWithSub;
                account.SubscriptionEndDate = DateTime.Now.AddDays(promo.AdditionalDays);
            }
            else if (account.Role == Role.UserWithSub)
            {
                account.SubscriptionEndDate = account.SubscriptionEndDate.Value.AddDays(promo.AdditionalDays);
            }

            _context.PromoCodes.Remove(promo);
            _context.Accounts.Update(account);
            _context.SaveChanges();
        }

        public DateTime CheckSubscriptionEndDate (int id)
        {
            var account = GetAccount(id);

            if (account.Role == Role.User)
            {
                throw new AppException("Нет действующей подписки");
            }
            else if (account.Role == Role.UserWithSub)
            {
                if (account.SubscriptionEndDate >= DateTime.Now)
                {
                    return account.SubscriptionEndDate.Value;
                }
                else
                {
                    account.Role = Role.User;
                    account.SubscriptionEndDate = null;
                    account.Updated = DateTime.UtcNow;

                    _context.Accounts.Update(account);
                    _context.SaveChanges();
                    throw new AppException("Срок действия подписки истек");
                }
            }
            else // if account.Role == Admin
            {
                return DateTime.MaxValue;
            }
        }

        // вспомогательные методы

        private Account GetAccount(int id)
        {
            var account = _context.Accounts.Find(id);
            if (account == null) throw new KeyNotFoundException("Аккаунт не найден");
            return account;
        }

        private string GenerateRandomString(int length)
        {
            using var rngCryptoServiceProvider = new RNGCryptoServiceProvider();
            var randomBytes = new byte[length];
            rngCryptoServiceProvider.GetBytes(randomBytes);
            // convert random bytes to hex string
            return BitConverter.ToString(randomBytes).Replace("-", "");
        }
    }
}
