﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using stockapi.Entities;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private readonly IList<Role> _roles;

    public AuthorizeAttribute(params Role[] roles)
    {
        _roles = roles ?? new Role[] { };
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var account = (Account)context.HttpContext.Items["Account"];
        if (account == null || (_roles.Any() && !_roles.Contains(account.Role)))
        {
            // вход не осуществлен или роль не подходит под ограничения запроса
            context.Result = new JsonResult(new { message = "Вы не авторизованы" }) { StatusCode = StatusCodes.Status401Unauthorized };
        }
    }
}